//! Document strip for the AW workspace. Order belongs to the window, not to the files.
use egui::{Color32, Id, RichText, Sense, Ui, vec2};

pub enum Action {
    Select(u64),
    Close(u64),
    New,
}

pub fn show(ui: &mut Ui, entries: &[(u64, String, bool)], accent: Color32, portuguese: bool) -> Option<Action> {
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
    egui::Panel::top("aw_documents")
        .exact_size(48.0)
        .frame(egui::Frame::NONE.fill(ui.visuals().faint_bg_color).inner_margin(egui::Margin::symmetric(8, 6)))
        .show(ui, |ui| {
            egui::ScrollArea::horizontal().id_salt("aw_document_scroll").auto_shrink([false, true]).show(ui, |ui| {
                ui.horizontal(|ui| {
                    for id in &order {
                        let Some((_, title, active)) = entries.iter().find(|e| e.0 == *id) else { continue };
                        ui.push_id(id, |ui| {
                            let fill = if *active { accent } else { ui.visuals().widgets.inactive.bg_fill };
                            egui::Frame::NONE.fill(fill).corner_radius(6.0).inner_margin(4.0).show(ui, |ui| {
                                ui.horizontal(|ui| {
                                    let color = if *active { Color32::WHITE } else { ui.visuals().text_color() };
                                    let response = ui
                                        .add(
                                            egui::Button::new(RichText::new(title).strong().color(color))
                                                .frame(false)
                                                .min_size(vec2(120.0, 26.0))
                                                .sense(Sense::click_and_drag()),
                                        )
                                        .on_hover_text(title)
                                        .on_hover_cursor(egui::CursorIcon::Grab);
                                    response.widget_info(|| egui::WidgetInfo::selected(egui::Role::Button, true, *active, title));
                                    if response.clicked() {
                                        action = Some(Action::Select(*id));
                                    }
                                    if response.drag_started() {
                                        ui.ctx().data_mut(|d| d.insert_temp(drag_id, *id));
                                    }
                                    if response.contains_pointer() && ui.input(|i| i.pointer.any_released()) {
                                        dropped = ui.ctx().data_mut(|d| d.get_temp::<u64>(drag_id)).map(|from| (from, *id));
                                    }
                                    let response = ui.add(egui::Button::new(RichText::new("×").color(color)).frame(false)).on_hover_text(close);
                                    response.widget_info(|| egui::WidgetInfo::labeled(egui::Role::Button, true, format!("{close}: {title}")));
                                    if response.clicked() {
                                        action = Some(Action::Close(*id));
                                    }
                                });
                            });
                        });
                    }
                    if ui.button("+").on_hover_text(new).clicked() {
                        action = Some(Action::New);
                    }
                });
            });
        });
    if let Some(dragged) = ui.ctx().data_mut(|d| d.get_temp::<u64>(drag_id))
        && let Some((_, title, _)) = entries.iter().find(|entry| entry.0 == dragged)
        && let Some(pos) = ui.input(|i| i.pointer.hover_pos())
        && ui.input(|i| i.pointer.any_down())
    {
        let painter = ui.ctx().layer_painter(egui::LayerId::new(egui::Order::Tooltip, Id::unique("aw_dragged_document")));
        let text = painter.layout_no_wrap(title.clone(), egui::FontId::proportional(14.0), Color32::WHITE);
        let rect = egui::Rect::from_min_size(pos + vec2(14.0, 14.0), text.size() + vec2(16.0, 10.0));
        painter.rect_filled(rect, 5.0, accent);
        painter.galley(rect.min + vec2(8.0, 5.0), text, Color32::WHITE);
        ui.ctx().set_cursor_icon(egui::CursorIcon::Grabbing);
    }
    if let Some((from, target)) = dropped {
        reorder(&mut order, from, target);
    }
    if ui.input(|i| i.pointer.any_released()) {
        ui.ctx().data_mut(|d| d.remove::<u64>(drag_id));
    }
    ui.ctx().data_mut(|d| d.insert_temp(order_id, order));
    action
}

fn reorder(order: &mut Vec<u64>, from: u64, target: u64) {
    if from == target {
        return;
    }
    if let (Some(a), Some(b)) = (order.iter().position(|id| *id == from), order.iter().position(|id| *id == target)) {
        order.remove(a);
        order.insert(b.min(order.len()), from);
    }
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
