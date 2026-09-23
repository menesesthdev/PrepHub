using PrepHub.Application.Contracts;

namespace PrepHub.Web.Models;

/// <summary>Dados iniciais para renderizar a tela de prova (shell + primeira questão).</summary>
public sealed record RealizarProvaViewModel(EstadoDaTentativaDto State, QuestaoDto FirstQuestion);
