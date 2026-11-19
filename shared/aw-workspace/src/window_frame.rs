//! Window buttons shared by standalone editors. The native host still handles unsaved work.
use egui::{Color32, Context, Rect, Sense, Stroke, ViewportCommand, pos2, vec2};
pub const RESERVED: f32 = 138.0;
pub fn show(ctx: &Context) {
    let (maximized, fullscreen) = ctx.input(|i| (i.viewport().maximized.unwrap_or(false), i.viewport().fullscreen.unwrap_or(false)));
    if fullscreen {
        return;
    }
    let screen = ctx.content_rect();
    let controls = Rect::from_min_size(pos2(screen.right() - RESERVED, screen.top()), vec2(RESERVED, 32.0));
    egui::Area::new(egui::Id::unique("aw_window_controls")).order(egui::Order::Foreground).fixed_pos(controls.min).show(ctx, |ui| {
        ui.spacing_mut().item_spacing = vec2(0.0, 0.0);
        ui.horizontal(|ui| {
            for (kind, label) in [(0, "Minimizar"), (1, if maximized { "Restaurar" } else { "Maximizar" }), (2, "Fechar janela")] {
                let (rect, response) = ui.allocate_exact_size(vec2(46.0, 32.0), Sense::click());
                response.widget_info(|| egui::WidgetInfo::labeled(egui::Role::Button, true, label));
                if response.hovered() {
                    ui.painter().rect_filled(rect, 0, if kind == 2 { Color32::from_rgb(196, 43, 28) } else { Color32::from_white_alpha(25) });
                }
                let p = ui.painter();
                let m = rect.center();
                let stroke = Stroke::new(1.0, Color32::WHITE);
                match kind {
                    0 => {
                        p.line_segment([m - vec2(5.0, 0.0), m + vec2(5.0, 0.0)], stroke);
                    }
                    1 => {
                        p.rect_stroke(Rect::from_center_size(m, vec2(10.0, 10.0)), 0, stroke, egui::StrokeKind::Inside);
                    }
                    _ => {
                        p.line_segment([m - vec2(5.0, 5.0), m + vec2(5.0, 5.0)], stroke);
                        p.line_segment([m + vec2(5.0, -5.0), m + vec2(-5.0, 5.0)], stroke);
                    }
                }
                if response.on_hover_text(label).clicked() {
                    ctx.send_viewport_cmd(match kind {
                        0 => ViewportCommand::Minimized(true),
                        1 => ViewportCommand::Maximized(!maximized),
                        _ => ViewportCommand::Close,
                    });
                }
            }
        });
    });
    if maximized {
        return;
    }
    if let Some(p) = ctx.input(|i| i.pointer.hover_pos())
        && let Some(direction) = resize_direction(screen, p)
    {
        ctx.set_cursor_icon(resize_cursor(direction));
        if ctx.input(|i| i.pointer.primary_pressed()) {
            ctx.send_viewport_cmd(ViewportCommand::BeginResize(direction));
        }
    }
}

/// Edges take priority even where the title bar or a document fills the client area.
pub fn resize_direction(rect: Rect, p: egui::Pos2) -> Option<egui::viewport::ResizeDirection> {
    use egui::viewport::ResizeDirection::*;
    if !rect.contains(p) {
        return None;
    }
    let band = 8.0;
    match (p.x <= rect.left() + band, p.x >= rect.right() - band, p.y <= rect.top() + band, p.y >= rect.bottom() - band) {
        (true, _, true, _) => Some(NorthWest),
        (_, true, true, _) => Some(NorthEast),
        (true, _, _, true) => Some(SouthWest),
        (_, true, _, true) => Some(SouthEast),
        (true, _, _, _) => Some(West),
        (_, true, _, _) => Some(East),
        (_, _, true, _) => Some(North),
        (_, _, _, true) => Some(South),
        _ => None,
    }
}
pub fn resize_cursor(direction: egui::viewport::ResizeDirection) -> egui::CursorIcon {
    use egui::viewport::ResizeDirection::*;
    match direction {
        North | South => egui::CursorIcon::ResizeVertical,
        East | West => egui::CursorIcon::ResizeHorizontal,
        NorthWest | SouthEast => egui::CursorIcon::ResizeNwSe,
        NorthEast | SouthWest => egui::CursorIcon::ResizeNeSw,
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn resize_covers_all_edges_without_consuming_document_center() {
        use egui::viewport::ResizeDirection::*;
        let rect = Rect::from_min_size(pos2(0.0, 0.0), vec2(800.0, 600.0));
        for (point, direction) in [
            (pos2(4.0, 4.0), NorthWest),
            (pos2(400.0, 4.0), North),
            (pos2(796.0, 4.0), NorthEast),
            (pos2(4.0, 300.0), West),
            (pos2(796.0, 300.0), East),
            (pos2(4.0, 596.0), SouthWest),
            (pos2(400.0, 596.0), South),
            (pos2(796.0, 596.0), SouthEast),
        ] {
            assert_eq!(resize_direction(rect, point), Some(direction));
        }
        assert_eq!(resize_direction(rect, pos2(400.0, 300.0)), None);
        assert_eq!(resize_direction(rect, pos2(-1.0, 300.0)), None);
    }
}
