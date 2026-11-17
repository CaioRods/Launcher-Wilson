//! Local profile image preferences and their preview card.

use egui::{Color32, Id, Rect, RichText, Sense, TextureHandle, Ui, vec2};
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(default)]
pub struct Profile {
    pub name: String,
    photo: Vec<u8>,
    banner: Vec<u8>,
    image: Vec<u8>,
    #[serde(skip)]
    pub error: Option<String>,
    #[serde(skip)]
    baseline: Option<u64>,
    #[serde(skip)]
    next_poll: Option<web_time::Instant>,
    #[serde(skip)]
    stamp: Option<(std::time::SystemTime, u64)>,
}

#[derive(Clone, Copy)]
pub enum Slot {
    Photo,
    Banner,
    Image,
}

impl Profile {
    /// Both AW apps share this local Windows-user profile; document content is never stored here.
    pub fn sync(&mut self) -> bool {
        #[cfg(not(target_arch = "wasm32"))]
        {
            let now = web_time::Instant::now();
            if self.next_poll.is_some_and(|next| now < next) {
                return false;
            }
            self.next_poll = Some(now + std::time::Duration::from_millis(750));
            if let Some(path) = shared_path() {
                return self.sync_path(&path);
            }
        }
        false
    }

    #[cfg(not(target_arch = "wasm32"))]
    fn sync_path(&mut self, path: &std::path::Path) -> bool {
        let current = self.fingerprint();
        if self.baseline.is_some_and(|previous| previous != current) {
            match self.save_to(path) {
                Ok(()) => {
                    self.baseline = Some(current);
                    self.stamp = file_stamp(path);
                }
                Err(error) => self.error = Some(format!("Não foi possível salvar o perfil: {error}")),
            }
        } else {
            let stamp = file_stamp(path);
            if self.baseline.is_some() && stamp.is_some() && self.stamp == stamp {
                return false;
            }
            match Self::read_from(path) {
                Ok(mut loaded) => {
                    let changed = self.baseline.is_none() || loaded.fingerprint() != current;
                    loaded.next_poll = self.next_poll;
                    loaded.stamp = stamp;
                    loaded.baseline = Some(loaded.fingerprint());
                    *self = loaded;
                    return changed;
                }
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                    // Migrate old app-local images instead of replacing them with an empty profile.
                    if self.baseline.is_none()
                        && (!self.photo.is_empty() || !self.name.is_empty())
                        && let Err(error) = self.save_to(path)
                    {
                        self.error = Some(error.to_string());
                    }
                    self.baseline = Some(current);
                }
                Err(error) => {
                    self.error = Some(format!("Não foi possível ler o perfil: {error}"));
                    self.baseline = Some(current);
                }
            }
        }
        false
    }

    #[cfg(not(target_arch = "wasm32"))]
    fn fingerprint(&self) -> u64 {
        use std::hash::{DefaultHasher, Hash as _, Hasher as _};
        let mut h = DefaultHasher::new();
        (&self.name, &self.photo, &self.banner, &self.image).hash(&mut h);
        h.finish()
    }

    #[cfg(not(target_arch = "wasm32"))]
    fn read_from(path: &std::path::Path) -> std::io::Result<Self> {
        use std::io::Read as _;
        let mut bytes = Vec::new();
        std::fs::File::open(path)?.take(4_000_001).read_to_end(&mut bytes)?;
        if bytes.len() > 4_000_000 {
            return Err(std::io::Error::other("Perfil muito grande"));
        }
        let profile: Self = serde_json::from_slice(&bytes)?;
        if profile.name.chars().count() > 100 || [&profile.photo, &profile.banner, &profile.image].iter().any(|b| b.len() > 500_000) {
            return Err(std::io::Error::other("Perfil inválido"));
        }
        Ok(profile)
    }

    #[cfg(not(target_arch = "wasm32"))]
    fn save_to(&self, path: &std::path::Path) -> std::io::Result<()> {
        let parent = path.parent().ok_or_else(|| std::io::Error::other("Perfil sem pasta"))?;
        std::fs::create_dir_all(parent)?;
        let temp = parent.join(format!("profile-{}.tmp", std::process::id()));
        let bytes = serde_json::to_vec(self)?;
        std::fs::write(&temp, bytes)?;
        // Rename replaces the file atomically, so the other app never reads a partial image.
        let result = std::fs::rename(&temp, path);
        if result.is_err() {
            let _ = std::fs::remove_file(temp);
        }
        result
    }

    /// Flush a final edit before the host saves preferences or exits.
    pub fn flush_shared(&self) -> Result<(), String> {
        #[cfg(not(target_arch = "wasm32"))]
        if self.baseline.is_some_and(|baseline| baseline != self.fingerprint())
            && let Some(path) = shared_path()
        {
            self.save_to(&path).map_err(|error| error.to_string())?;
        }
        Ok(())
    }

    pub fn avatar(&self, ui: &Ui, rect: Rect) -> bool {
        preview(ui, Id::unique("aw_profile_avatar"), rect, &self.photo)
    }

    fn image(&mut self, slot: Slot) -> &mut Vec<u8> {
        match slot {
            Slot::Photo => &mut self.photo,
            Slot::Banner => &mut self.banner,
            Slot::Image => &mut self.image,
        }
    }

    pub fn import(&mut self, slot: Slot, path: &str, portuguese: bool) -> Result<(), String> {
        use std::io::Read as _;
        const MAX_BYTES: u64 = 20 * 1024 * 1024;
        let error = || {
            if portuguese { "Escolha uma imagem PNG ou JPEG válida de até 20 MB." } else { "Choose a valid PNG or JPEG image up to 20 MB." }
                .to_owned()
        };
        let path = std::path::Path::new(path);
        if !path.is_file() {
            return Err(error());
        }
        let mut data = Vec::new();
        std::fs::File::open(path).and_then(|file| file.take(MAX_BYTES + 1).read_to_end(&mut data)).map_err(|_| error())?;
        if data.len() as u64 > MAX_BYTES {
            return Err(error());
        }
        let format = image::guess_format(&data).map_err(|_| error())?;
        if !matches!(format, image::ImageFormat::Jpeg | image::ImageFormat::Png) {
            return Err(error());
        }
        let mut reader = image::ImageReader::with_format(std::io::Cursor::new(data), format);
        let mut limits = image::Limits::default();
        limits.max_image_width = Some(8192);
        limits.max_image_height = Some(8192);
        limits.max_alloc = Some(128 * 1024 * 1024);
        reader.limits(limits);
        let decoded = reader.decode().map_err(|_| error())?;
        let preview = match slot {
            Slot::Photo => decoded.resize_to_fill(144, 144, image::imageops::FilterType::Triangle),
            Slot::Banner => decoded.resize_to_fill(960, 250, image::imageops::FilterType::Triangle),
            Slot::Image => decoded.thumbnail(640, 320),
        };
        let mut jpeg = Vec::new();
        image::codecs::jpeg::JpegEncoder::new_with_quality(&mut jpeg, 85).encode_image(&preview.to_rgb8()).map_err(|_| error())?;
        *self.image(slot) = jpeg;
        Ok(())
    }
}

fn preview(ui: &Ui, id: Id, rect: Rect, bytes: &[u8]) -> bool {
    use std::hash::{DefaultHasher, Hash as _, Hasher as _};
    if bytes.is_empty() || bytes.len() > 500_000 {
        ui.ctx().data_mut(|data| data.remove::<(u64, TextureHandle)>(id));
        return false;
    }
    let mut hasher = DefaultHasher::new();
    bytes.hash(&mut hasher);
    let hash = hasher.finish();
    let cached = ui.ctx().data_mut(|data| data.get_temp::<(u64, TextureHandle)>(id));
    let texture = if let Some((_, texture)) = cached.filter(|(previous, _)| *previous == hash) {
        texture
    } else {
        let mut reader = image::ImageReader::with_format(std::io::Cursor::new(bytes), image::ImageFormat::Jpeg);
        let mut limits = image::Limits::default();
        limits.max_image_width = Some(960);
        limits.max_image_height = Some(320);
        limits.max_alloc = Some(8 * 1024 * 1024);
        reader.limits(limits);
        let Ok(image) = reader.decode() else { return false };
        let rgba = image.into_rgba8();
        let pixels = egui::ColorImage::from_rgba_unmultiplied([rgba.width() as usize, rgba.height() as usize], &rgba);
        let texture = ui.ctx().load_texture("profile", pixels, egui::TextureOptions::LINEAR);
        ui.ctx().data_mut(|data| data.insert_temp(id, (hash, texture.clone())));
        texture
    };
    let scale = (rect.width() / texture.size_vec2().x).min(rect.height() / texture.size_vec2().y);
    let fitted = Rect::from_center_size(rect.center(), texture.size_vec2() * scale);
    let radius = if id == Id::unique("aw_profile_avatar") { (rect.width() / 2.0).min(255.0) as u8 } else { 0 };
    egui::Image::new((texture.id(), texture.size_vec2())).corner_radius(radius).paint_at(ui, fitted);
    true
}

#[cfg(not(target_arch = "wasm32"))]
fn file_stamp(path: &std::path::Path) -> Option<(std::time::SystemTime, u64)> {
    let metadata = std::fs::metadata(path).ok()?;
    Some((metadata.modified().ok()?, metadata.len()))
}

#[cfg(not(target_arch = "wasm32"))]
fn shared_path() -> Option<std::path::PathBuf> {
    #[cfg(target_os = "windows")]
    let base = std::env::var_os("APPDATA").map(std::path::PathBuf::from);
    #[cfg(not(target_os = "windows"))]
    let base = std::env::var_os("XDG_CONFIG_HOME")
        .map(std::path::PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|home| std::path::PathBuf::from(home).join(".config")));
    base.map(|base| base.join("AlimentosWilson").join("profile.json"))
}

pub fn card(ui: &mut Ui, profile: &mut Profile, name: &mut String, portuguese: bool, can_import: bool, can_edit_name: bool) -> Option<Slot> {
    let label = |pt, en| if portuguese { pt } else { en };
    let accent = ui.visuals().selection.bg_fill;
    let mut pick = None;
    ui.push_id("local_profile", |ui| {
        let width = ui.available_width().clamp(120.0, 570.0);
        ui.heading(label("Meu perfil", "My profile"));
        ui.add_space(6.0);
        let (banner, _) = ui.allocate_exact_size(vec2(width, width * 0.26), Sense::hover());
        ui.painter().rect_filled(banner, 8.0, accent);
        preview(ui, ui.scope_id().with("banner"), banner, &profile.banner);
        ui.add_space(8.0);
        ui.horizontal(|ui| {
            let (photo, _) = ui.allocate_exact_size(vec2(62.0, 62.0), Sense::hover());
            if !preview(ui, ui.scope_id().with("photo"), photo, &profile.photo) {
                ui.painter().rect_filled(photo, 8.0, accent);
                let initials: String = name.split_whitespace().take(2).filter_map(|part| part.chars().next()).flat_map(char::to_uppercase).collect();
                ui.painter().text(photo.center(), egui::Align2::CENTER_CENTER, initials, egui::FontId::proportional(22.0), Color32::WHITE);
            }
            ui.vertical(|ui| {
                let name_label = ui.label(RichText::new(label("Nome", "Name")).strong());
                ui.add_enabled(can_edit_name, egui::TextEdit::singleline(name).char_limit(100).desired_width(240.0)).labelled_by(name_label.id);
            });
        });
        ui.add_space(6.0);
        for (slot, title, action) in [
            (Slot::Photo, label("Foto", "Photo"), label("Escolher foto", "Choose photo")),
            (Slot::Banner, label("Banner", "Banner"), label("Escolher banner", "Choose banner")),
            (Slot::Image, label("Imagem adicional", "Additional image"), label("Escolher imagem", "Choose image")),
        ] {
            let data = profile.image(slot);
            ui.push_id(title, |ui| {
                ui.horizontal_wrapped(|ui| {
                    if ui.add_enabled(can_import, egui::Button::new(action)).clicked() {
                        pick = Some(slot);
                    }
                    if !data.is_empty() && ui.button(label("Remover", "Remove")).clicked() {
                        data.clear();
                    }
                });
            });
        }
        if pick.is_some() {
            profile.error = None;
        }
        if let Some(error) = &profile.error {
            ui.colored_label(ui.visuals().error_fg_color, error);
        }
        if !profile.image.is_empty() {
            ui.add_space(6.0);
            let (rect, _) = ui.allocate_exact_size(vec2(width.min(330.0), 154.0), Sense::hover());
            preview(ui, ui.scope_id().with("image"), rect, &profile.image);
        }
    });
    profile.name = name.clone();
    pick
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn two_running_apps_receive_profile_updates_and_removals() {
        let folder = std::env::temp_dir().join(format!("aw-live-profile-{}", std::process::id()));
        let path = folder.join("profile.json");
        let mut excel = Profile { name: "First".into(), photo: vec![1], ..Default::default() };
        excel.sync_path(&path);
        let mut word = Profile::default();
        assert!(word.sync_path(&path));
        assert_eq!(word.name, "First");
        word.name = "Updated".into();
        word.photo.clear();
        word.sync_path(&path);
        assert!(excel.sync_path(&path));
        assert_eq!(excel.name, "Updated");
        assert!(excel.photo.is_empty());
        assert!(!excel.sync_path(&path), "unchanged data must not overwrite the editing author every frame");
        std::fs::remove_dir_all(folder).unwrap();
    }

    #[test]
    fn imported_photo_is_drawn_in_corner_avatar() {
        let mut jpeg = Vec::new();
        let image = image::RgbImage::from_pixel(144, 144, image::Rgb([30, 90, 160]));
        image::codecs::jpeg::JpegEncoder::new(&mut jpeg).encode_image(&image).unwrap();
        let profile = Profile { photo: jpeg, ..Default::default() };
        let ctx = egui::Context::default();
        let output = ctx.run_ui(egui::RawInput::default(), |ui| {
            assert!(profile.avatar(ui, Rect::from_min_size(egui::Pos2::ZERO, vec2(28.0, 28.0))));
        });
        assert!(!output.textures_delta.set.is_empty(), "the profile photo must be uploaded, not replaced by initials");
        output.drop_without_applying_deltas();
    }

    #[test]
    fn shared_profile_roundtrip_keeps_all_fields_and_removals() {
        let folder = std::env::temp_dir().join(format!("aw-shared-profile-{}", std::process::id()));
        let path = folder.join("profile.json");
        let first = Profile { name: "Test User".into(), photo: vec![1], banner: vec![2], image: vec![3], ..Default::default() };
        first.save_to(&path).unwrap();
        let mut second = Profile::read_from(&path).unwrap();
        assert_eq!(second.fingerprint(), first.fingerprint());
        second.photo.clear();
        second.name = "Another Name".into();
        second.save_to(&path).unwrap();
        let reloaded = Profile::read_from(&path).unwrap();
        assert!(reloaded.photo.is_empty());
        assert_eq!(reloaded.name, "Another Name");
        assert_eq!(reloaded.banner, first.banner);
        std::fs::remove_dir_all(folder).unwrap();
    }

    #[test]
    fn import_keeps_independent_slots_and_serializes_saved_image() {
        let folder = std::env::temp_dir().join(format!("craft-profile-{}", std::process::id()));
        std::fs::create_dir_all(&folder).unwrap();
        let path = folder.join("profile.png");
        image::RgbImage::from_pixel(32, 24, image::Rgb([40, 80, 180])).save(&path).unwrap();
        let mut profile = Profile::default();
        profile.import(Slot::Photo, path.to_str().unwrap(), true).unwrap();
        let serialized = serde_json::to_string(&profile).unwrap();
        std::fs::remove_dir_all(folder).unwrap();
        let mut restored: Profile = serde_json::from_str(&serialized).unwrap();
        assert!(restored.banner.is_empty() && restored.image.is_empty());
        let thumb = image::load_from_memory(&restored.photo).unwrap();
        assert_eq!((thumb.width(), thumb.height()), (144, 144));
        assert!(restored.import(Slot::Photo, path.to_str().unwrap(), true).is_err());
        assert_eq!(restored.photo, profile.photo);
    }
}
