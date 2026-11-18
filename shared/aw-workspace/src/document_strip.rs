//! Compact document tabs above the application's menus, with explicit host colours.
use egui::{Color32, FontId, Id, Rect, Sense, Stroke, Ui, pos2, vec2};

pub enum Action {
    Select(u64),
    Close(u64),
    New,
    Home,
}

/// The active tab follows the host's light/dark palette; the outer strip stays graphite.
#[derive(Clone, Copy)]
pub struct Style {
    pub brand: &'static str,
    pub accent: Color32,
    pub active: Color32,
    pub text: Color32,
    pub on_accent: Color32,
    /// Reserved width for the host avatar and native window controls.
    pub trailing_width: f32,
    pub logo: Option<egui::TextureId>,
}

pub fn show(ui: &mut Ui, entries: &[(u64, String, bool)], accent: Color32, portuguese: bool) -> Option<Action> {
    show_with_style(
        ui,
        entries,
        Style {
            brand: "AW",
            accent,
            active: Color32::WHITE,
            text: Color32::from_gray(35),
            on_accent: Color32::WHITE,
            trailing_width: 44.0,
            logo: None,
        },
        portuguese,
        |_| {},
    )
}

pub fn show_with_style(ui: &mut Ui, entries: &[(u64, String, bool)], style: Style, portuguese: bool, right: impl FnOnce(&mut Ui)) -> Option<Action> {
    let close = if portuguese { "Fechar documento" } else { "Close document" };
    let new = if portuguese { "Novo documento" } else { "New document" };
    let order_id = Id::unique("aw_document_order");
    let drag_id = Id::unique("aw_document_drag");
    let mut order = ui.ctx().data_mut(|d| d.get_temp::<Vec<u64>>(order_id)).unwrap_or_default();
    order.retain(|id| entries.iter().any(|e| e.0 == *id));
    for (id, _, _) in entries {
        if !order.contains(id) {
            order.push(*id);
        }
    }
    let mut action = None;
    let mut dropped = None;
    let bar = Color32::from_rgb(59, 62, 72);
    egui::Panel::top("aw_documents").exact_size(32.0).frame(egui::Frame::NONE.fill(bar)).show(ui, |ui| {
        let drag = ui.interact(ui.max_rect(), ui.scope_id().with("window_drag"), Sense::click_and_drag());
        if drag.drag_started() {
            ui.ctx().send_viewport_cmd(egui::ViewportCommand::StartDrag);
        }
        if drag.double_clicked() {
            let maximized = ui.input(|i| i.viewport().maximized.unwrap_or(false));
            ui.ctx().send_viewport_cmd(egui::ViewportCommand::Maximized(!maximized));
        }
        let bar_rect = ui.max_rect();
        let brand = Rect::from_min_size(bar_rect.min, vec2(110.0_f32.min(bar_rect.width()), 32.0));
        ui.painter().rect_filled(brand.shrink2(vec2(4.0, 3.0)), 7, style.accent);
        ui.painter().text(
            if style.logo.is_some() { pos2(brand.left() + 38.0, brand.center().y) } else { brand.center() },
            if style.logo.is_some() { egui::Align2::LEFT_CENTER } else { egui::Align2::CENTER_CENTER },
            style.brand,
            FontId::proportional(12.5),
            style.on_accent,
        );
        if let Some(logo) = style.logo {
            ui.painter().image(
                logo,
                Rect::from_center_size(pos2(brand.left() + 20.0, brand.center().y), vec2(22.0, 22.0)),
                Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)),
                Color32::WHITE,
            );
        }
        let response = ui.interact(brand, ui.scope_id().with("brand"), Sense::click());
        response.widget_info(|| egui::WidgetInfo::labeled(egui::Role::Button, true, style.brand));
        if response.clicked() {
            action = Some(Action::Home);
        }
        let trailing = style.trailing_width.clamp(0.0, (bar_rect.width() - brand.width()).max(0.0));
        let right_rect = Rect::from_min_max(pos2(bar_rect.right() - trailing, bar_rect.top()), bar_rect.right_bottom());
        ui.scope_builder(egui::UiBuilder::new().max_rect(right_rect).layout(egui::Layout::right_to_left(egui::Align::Center)), |ui| {
            ui.spacing_mut().item_spacing = vec2(0.0, 0.0);
            ui.add_space(8.0);
            right(ui);
        });
        let tabs_rect = Rect::from_min_max(brand.right_top(), right_rect.left_bottom());
        ui.scope_builder(egui::UiBuilder::new().max_rect(tabs_rect).layout(egui::Layout::top_down(egui::Align::Min)), |ui| {
            ui.set_clip_rect(ui.clip_rect().intersect(tabs_rect));
            ui.spacing_mut().item_spacing = vec2(0.0, 0.0);
            egui::ScrollArea::horizontal()
                .id_salt("aw_document_scroll")
                .max_height(32.0)
                .scroll_bar_visibility(egui::scroll_area::ScrollBarVisibility::AlwaysHidden)
                .auto_shrink([false, false])
                .show(ui, |ui| {
                    ui.horizontal_top(|ui| {
                        for id in &order {
                            let Some((_, title, active)) = entries.iter().find(|entry| entry.0 == *id) else { continue };
                            ui.push_id(id, |ui| {
                                let color = if *active { style.text } else { Color32::from_gray(230) };
                                let mut job =
                                    egui::text::LayoutJob::simple_singleline(title.chars().take(160).collect(), FontId::proportional(12.0), color);
                                job.wrap = egui::text::TextWrapping::truncate_at_width(210.0);
                                let text = ui.painter().layout_job(job);
                                let width = (text.size().x + 56.0).clamp(150.0, 270.0);
                                let (tab, _) = ui.allocate_exact_size(vec2(width, 32.0), Sense::hover());
                                let duration = if ui.style().animation_time > 0.0 { 0.12 } else { 0.0 };
                                let x = ui.ctx().animate_value_with_time(ui.scope_id().with("position"), tab.left(), duration);
                                let tab = tab.translate(vec2(x - tab.left(), 0.0));
                                let close_rect = Rect::from_center_size(pos2(tab.right() - 13.0, tab.center().y), vec2(22.0, 24.0));
                                let body = Rect::from_min_max(tab.min, pos2(close_rect.left() - 2.0, tab.bottom()));
                                let response = ui
                                    .interact(body, ui.scope_id().with("document"), Sense::click_and_drag())
                                    .on_hover_text(title)
                                    .on_hover_cursor(egui::CursorIcon::Grab);
                                let fill = if *active {
                                    style.active
                                } else if response.hovered() {
                                    Color32::from_rgb(80, 84, 94)
                                } else {
                                    bar
                                };
                                ui.painter().rect_filled(tab.shrink2(vec2(2.0, 3.0)), 7, fill);
                                let icon = Rect::from_center_size(pos2(tab.left() + 14.0, tab.center().y), vec2(10.0, 13.0));
                                if let Some(logo) = style.logo {
                                    ui.painter().image(
                                        logo,
                                        Rect::from_center_size(icon.center(), vec2(16.0, 16.0)),
                                        Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)),
                                        Color32::WHITE,
                                    );
                                } else {
                                    ui.painter().rect_stroke(
                                        icon,
                                        1.0,
                                        Stroke::new(1.0, if *active { style.accent } else { color }),
                                        egui::StrokeKind::Inside,
                                    );
                                    ui.painter().line_segment(
                                        [icon.left_top() + vec2(2.0, 4.0), icon.right_top() + vec2(-2.0, 4.0)],
                                        Stroke::new(1.0, color),
                                    );
                                }
                                ui.painter().galley(pos2(tab.left() + 26.0, tab.center().y - text.size().y / 2.0), text, color);
                                response.widget_info(|| egui::WidgetInfo::selected(egui::Role::Button, true, *active, title));
                                if response.clicked() {
                                    action = Some(Action::Select(*id));
                                }
                                if response.drag_started() {
                                    ui.ctx().data_mut(|data| data.insert_temp(drag_id, *id));
                                }
                                if response.contains_pointer() && ui.input(|input| input.pointer.any_released()) {
                                    dropped = ui.ctx().data_mut(|data| data.get_temp::<u64>(drag_id)).map(|from| (from, *id));
                                }
                                let response = ui
                                    .interact(close_rect, ui.scope_id().with("close"), Sense::click())
                                    .on_hover_text(close)
                                    .on_hover_cursor(egui::CursorIcon::PointingHand);
                                if response.hovered() {
                                    ui.painter().rect_filled(close_rect.shrink(2.0), 3.0, Color32::from_white_alpha(35));
                                }
                                ui.painter().text(close_rect.center(), egui::Align2::CENTER_CENTER, "×", FontId::proportional(16.0), color);
                                response.widget_info(|| egui::WidgetInfo::labeled(egui::Role::Button, true, format!("{close}: {title}")));
                                if response.clicked() {
                                    action = Some(Action::Close(*id));
                                }
                            });
                        }
                        let (rect, response) = ui.allocate_exact_size(vec2(34.0, 32.0), Sense::click());
                        ui.painter().text(rect.center(), egui::Align2::CENTER_CENTER, "+", FontId::proportional(22.0), Color32::from_gray(230));
                        response.widget_info(|| egui::WidgetInfo::labeled(egui::Role::Button, true, new));
                        if response.on_hover_text(new).clicked() {
                            action = Some(Action::New);
                        }
                    });
                });
        });
    });
    if let Some(dragged) = ui.ctx().data_mut(|data| data.get_temp::<u64>(drag_id))
        && let Some((_, title, _)) = entries.iter().find(|entry| entry.0 == dragged)
        && let Some(pos) = ui.input(|input| input.pointer.hover_pos())
        && ui.input(|input| input.pointer.any_down())
    {
        let painter = ui.ctx().layer_painter(egui::LayerId::new(egui::Order::Tooltip, Id::unique("aw_dragged_document")));
        let text = painter.layout_no_wrap(title.clone(), FontId::proportional(12.0), style.on_accent);
        let rect = Rect::from_min_size(pos + vec2(14.0, 14.0), text.size() + vec2(16.0, 10.0));
        painter.rect_filled(rect, 4.0, style.accent);
        painter.galley(rect.min + vec2(8.0, 5.0), text, style.on_accent);
        ui.ctx().set_cursor_icon(egui::CursorIcon::Grabbing);
    }
    if let Some((from, to)) = dropped {
        reorder(&mut order, from, to);
    }
    if ui.input(|input| input.pointer.any_released()) {
        ui.ctx().data_mut(|data| data.remove::<u64>(drag_id));
    }
    ui.ctx().data_mut(|data| data.insert_temp(order_id, order));
    action
}

fn reorder(order: &mut Vec<u64>, from: u64, to: u64) {
    if from == to {
        return;
    }
    if let (Some(a), Some(b)) = (order.iter().position(|id| *id == from), order.iter().position(|id| *id == to)) {
        order.remove(a);
        order.insert(b.min(order.len()), from);
    }
}

/// Caches one of the host's embedded, attributed PNG app marks.
pub fn logo(ctx: &egui::Context, name: &'static str, png: &[u8]) -> Option<egui::TextureId> {
    let id = Id::unique(("aw_app_logo", name));
    if let Some(texture) = ctx.data_mut(|data| data.get_temp::<egui::TextureHandle>(id)) {
        return Some(texture.id());
    }
    let image = image::load_from_memory_with_format(png, image::ImageFormat::Png).ok()?.into_rgba8();
    let pixels = egui::ColorImage::from_rgba_unmultiplied([image.width() as usize, image.height() as usize], image.as_raw());
    let texture = ctx.load_texture(name, pixels, egui::TextureOptions::LINEAR);
    let texture_id = texture.id();
    ctx.data_mut(|data| data.insert_temp(id, texture));
    Some(texture_id)
}

#[cfg(test)]
mod tests {
    fn frame(
        ctx: &egui::Context,
        entries: &[(u64, String, bool)],
        events: Vec<egui::Event>,
    ) -> (Option<super::Action>, Vec<egui::epaint::ClippedShape>) {
        let mut action = None;
        let input =
            egui::RawInput { screen_rect: Some(egui::Rect::from_min_size(egui::Pos2::ZERO, egui::vec2(800.0, 200.0))), events, ..Default::default() };
        let mut output = ctx.run_ui(input, |ui| {
            action = super::show(ui, entries, egui::Color32::DARK_RED, false);
        });
        let shapes = std::mem::take(&mut output.shapes);
        output.drop_without_applying_deltas();
        (action, shapes)
    }

    fn text_at(shapes: &[egui::epaint::ClippedShape], text: &str) -> egui::Pos2 {
        shapes
            .iter()
            .find_map(|s| match &s.shape {
                egui::Shape::Text(shape) if shape.galley.job.text == text => Some(shape.pos + shape.galley.size() / 2.0),
                _ => None,
            })
            .unwrap_or_else(|| panic!("missing tab text {text}"))
    }

    fn pointer(pos: egui::Pos2, pressed: bool) -> egui::Event {
        egui::Event::PointerButton { pos, button: egui::PointerButton::Primary, pressed, modifiers: egui::Modifiers::NONE }
    }

    #[test]
    fn one_document_has_a_visible_tab_and_close_button() {
        let ctx = egui::Context::default();
        let entries = vec![(1, "First".into(), true)];
        let (_, shapes) = frame(&ctx, &entries, vec![]);
        assert!(text_at(&shapes, "First").y < 48.0);
        let close = text_at(&shapes, "×");
        frame(&ctx, &entries, vec![egui::Event::PointerMoved(close), pointer(close, true)]);
        let (action, _) = frame(&ctx, &entries, vec![pointer(close, false)]);
        assert!(matches!(action, Some(super::Action::Close(1))));
    }

    #[test]
    fn active_tab_is_light_and_compact_even_with_dark_global_visuals() {
        let ctx = egui::Context::default();
        ctx.set_visuals(egui::Visuals::dark());
        let (_, shapes) = frame(&ctx, &[(1, "First".into(), true)], vec![]);
        let title = text_at(&shapes, "First");
        assert!(title.y < 32.0);
        assert!(shapes.iter().any(|shape| matches!(&shape.shape, egui::Shape::Rect(rect) if rect.fill == egui::Color32::WHITE && rect.rect.contains(title) && rect.rect.height() == 26.0)));
    }

    #[test]
    fn tabs_and_plus_align_with_brand_with_tall_host_controls() {
        let ctx = egui::Context::default();
        ctx.all_styles_mut(|style| style.spacing.interact_size.y = 48.0);
        for entries in [vec![], vec![(1, "First".into(), true)], (0..12).map(|id| (id, format!("Document {id}"), id == 0)).collect()] {
            let (_, shapes) = frame(&ctx, &entries, vec![]);
            assert!((text_at(&shapes, "AW").y - 16.0).abs() < 0.1);
            assert!((text_at(&shapes, "+").y - 16.0).abs() < 0.1);
            if entries.len() == 1 {
                assert!((text_at(&shapes, "First").y - 16.0).abs() < 0.1);
            }
        }
    }

    #[test]
    fn pointer_drag_reorders_tabs() {
        let ctx = egui::Context::default();
        let entries = vec![(1, "First".into(), true), (2, "Second".into(), false)];
        let (_, shapes) = frame(&ctx, &entries, vec![]);
        let from = text_at(&shapes, "First");
        let target = text_at(&shapes, "Second");
        frame(&ctx, &entries, vec![egui::Event::PointerMoved(from), pointer(from, true)]);
        frame(&ctx, &entries, vec![egui::Event::PointerMoved(target)]);
        frame(&ctx, &entries, vec![pointer(target, false)]);
        let order = ctx.data_mut(|d| d.get_temp::<Vec<u64>>(egui::Id::unique("aw_document_order"))).unwrap();
        assert_eq!(order, [2, 1]);
    }

    #[test]
    fn drag_moves_both_directions_without_losing_documents() {
        let mut ids = vec![1, 2, 3];
        super::reorder(&mut ids, 1, 3);
        assert_eq!(ids, [2, 3, 1]);
        super::reorder(&mut ids, 1, 2);
        assert_eq!(ids, [1, 2, 3]);
        super::reorder(&mut ids, 9, 1);
        assert_eq!(ids, [1, 2, 3]);
    }
}
