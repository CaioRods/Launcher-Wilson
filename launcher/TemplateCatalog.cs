using System;

// Stable model identifiers are shared with the native file.new command. Content is original.
internal sealed class WorkspaceTemplate
{
    internal readonly int App;internal readonly string Key,Title,Description;
    internal WorkspaceTemplate(int app,string key,string title,string description){App=app;Key=key;Title=title;Description=description;}
}
internal static class TemplateCatalog
{
    internal static readonly WorkspaceTemplate[] Items={
        new WorkspaceTemplate(0,"aw_budget","Orçamento mensal","Receitas, despesas e saldo em reais."),
        new WorkspaceTemplate(0,"aw_sales","Controle de vendas","Pedidos, quantidades e faturamento."),
        new WorkspaceTemplate(0,"aw_stock","Controle de estoque","Entradas, saídas e saldo por produto."),
        new WorkspaceTemplate(0,"aw_tasks","Planejamento de tarefas","Responsáveis, prazos e andamento."),
        new WorkspaceTemplate(0,"recebimento","Conferência de recebimento","Pedidos e quantidades recebidas."),
        new WorkspaceTemplate(0,"aw_cash","Fluxo de caixa","Movimentações e saldo disponível."),
        new WorkspaceTemplate(1,"aw_report","Relatório profissional","Resumo, resultados e próximos passos."),
        new WorkspaceTemplate(1,"aw_letter","Carta profissional","Uma comunicação clara e organizada."),
        new WorkspaceTemplate(1,"aw_minutes","Ata de reunião","Pauta, decisões e responsáveis."),
        new WorkspaceTemplate(1,"aw_proposal","Proposta comercial","Escopo, investimento e condições."),
        new WorkspaceTemplate(1,"aw_resume","Currículo","Experiência, formação e habilidades."),
        new WorkspaceTemplate(1,"aw_project","Plano de projeto","Objetivos, entregas e cronograma.")
    };
}
