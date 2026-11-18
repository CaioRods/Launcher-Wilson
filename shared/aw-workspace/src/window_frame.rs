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
        && !controls.contains(p)
        && ctx.input(|i| i.pointer.primary_pressed())
    {
        use egui::viewport::ResizeDirection::*;
        let l = p.x <= screen.left() + 5.0;
        let r = p.x >= screen.right() - 5.0;
        let t = p.y <= screen.top() + 5.0;
        let b = p.y >= screen.bottom() - 5.0;
        let direction = match (l, r, t, b) {
            (true, _, true, _) => Some(NorthWest),
            (_, true, true, _) => Some(NorthEast),
            (true, _, _, true) => Some(SouthWest),
            (_, true, _, true) => Some(SouthEast),
            (true, _, _, _) => Some(West),
            (_, true, _, _) => Some(East),
            (_, _, true, _) => Some(North),
            (_, _, _, true) => Some(South),
            _ => None,
        };
        if let Some(direction) = direction {
            ctx.send_viewport_cmd(ViewportCommand::BeginResize(direction));
        }
    }
}
