//! Shared dimensions, selection treatment and red background for the File sidebar.
use egui::{Align2, Color32, FontId, Painter, Rect, Response, Sense, Stroke, Ui, pos2, vec2};

pub const WIDTH: f32 = 196.0;

pub fn background(ui: &Ui, rect: Rect, dark: bool) {
    // Nearly opaque tinted glass keeps white labels readable without flattening the gradient.
    let stops = if dark {
        [
            Color32::from_rgba_unmultiplied(104, 57, 64, 246),
            Color32::from_rgba_unmultiplied(66, 37, 49, 242),
            Color32::from_rgba_unmultiplied(34, 26, 38, 250),
        ]
    } else {
        [
            Color32::from_rgba_unmultiplied(177, 63, 60, 246),
            Color32::from_rgba_unmultiplied(133, 46, 52, 240),
            Color32::from_rgba_unmultiplied(67, 31, 44, 250),
        ]
    };
    let mut mesh = egui::Mesh::default();
    for (y, color) in [(rect.top(), stops[0]), (rect.center().y, stops[1]), (rect.bottom(), stops[2])] {
        mesh.colored_vertex(pos2(rect.left(), y), color);
        mesh.colored_vertex(pos2(rect.right(), y), color);
    }
    for top in [0, 2] {
        mesh.add_triangle(top, top + 1, top + 3);
        mesh.add_triangle(top, top + 3, top + 2);
    }
    ui.painter().add(egui::Shape::mesh(mesh));
    let light = Rect::from_center_size(pos2(rect.right(), rect.top() + rect.height() * 0.18), vec2(rect.width() * 0.9, rect.height() * 0.4));
    ui.painter().add(egui::epaint::RectShape::filled(light, 32, Color32::from_rgba_unmultiplied(255, 215, 180, 17)).with_blur_width(45.0));
    ui.painter().line_segment([rect.right_top(), rect.right_bottom()], Stroke::new(1.0, Color32::from_white_alpha(30)));
}

/// Keeps the fixed Account/Options footer clear when the window is too short for every command.
pub fn scroll_main(ui: &mut Ui, id: &'static str, contents: impl FnOnce(&mut Ui)) {
    let height = (ui.available_height() - 82.0).max(0.0);
    egui::ScrollArea::vertical().id_salt(id).max_height(height).auto_shrink([false, true]).show(ui, contents);
}

pub fn row(ui: &mut Ui, label: &str, selected: bool, enabled: bool, rtl: bool, icon: impl FnOnce(&Painter, Rect, Color32)) -> Response {
    let (rect, response) = ui.add_enabled_ui(enabled, |ui| ui.allocate_exact_size(vec2(WIDTH, 38.0), Sense::click())).inner;
    let text = if enabled { Color32::WHITE } else { Color32::from_gray(180) };
    let fill = Color32::from_black_alpha(if selected { 46 } else { 30 });
    let pill = rect.shrink2(vec2(10.0, 2.0));
    if selected || (enabled && (response.hovered() || response.has_focus())) {
        ui.painter().rect_filled(pill, 9, fill);
    }
    let x = |offset: f32| if rtl { rect.right() - offset } else { rect.left() + offset };
    if selected {
        ui.painter().rect_filled(Rect::from_center_size(pos2(x(16.0), rect.center().y), vec2(3.0, 14.0)), 2, Color32::from_rgb(238, 198, 101));
    }
    icon(ui.painter(), Rect::from_center_size(pos2(x(30.0), rect.center().y), vec2(17.0, 17.0)), text);
    ui.painter().text(
        pos2(x(50.0), rect.center().y),
        if rtl { Align2::RIGHT_CENTER } else { Align2::LEFT_CENTER },
        label,
        FontId::proportional(14.0),
        text,
    );
    response.widget_info(|| egui::WidgetInfo::selected(egui::Role::Button, enabled, selected, label));
    response
}

pub fn separator(ui: &mut Ui) {
    let (rect, _) = ui.allocate_exact_size(vec2(WIDTH, 17.0), Sense::hover());
    ui.painter().line_segment(
        [pos2(rect.left() + 20.0, rect.center().y), pos2(rect.right() - 20.0, rect.center().y)],
        Stroke::new(1.0, Color32::from_white_alpha(90)),
    );
}
