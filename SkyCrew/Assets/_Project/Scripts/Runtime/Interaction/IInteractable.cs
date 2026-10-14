using SkyCrew.Domain.Interaction;
using UnityEngine;

namespace SkyCrew.Interaction
{
    /// <summary>
    /// Algo com que o jogador pode interagir. A descoberta (Interactor) fica separada
    /// da execucao; a validacao usa <see cref="InteractionRules"/> do dominio.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Acao disponivel considerando o que o jogador segura.</summary>
        InteractionVerb GetVerb(Interactor interactor);

        /// <summary>Texto exibido ("Pegar garrafa").</summary>
        string GetPrompt(Interactor interactor);

        /// <summary>Tipo de item aceito (Deliver/Dispose/PlaceInZone); null = qualquer.</summary>
        string GetAcceptedItemType(Interactor interactor);

        /// <summary>Disponivel agora (ex.: passageiro nao pediu nada).</summary>
        bool IsAvailable(Interactor interactor);

        /// <summary>Preferencia entre alvos validos (InteractionPriorities).</summary>
        int GetPriority(Interactor interactor);

        /// <summary>Ponto usado para distancia/angulo.</summary>
        Vector3 InteractionPoint { get; }

        /// <summary>Executa (ja validado). Retorna true se algo mudou no mundo.</summary>
        bool Execute(Interactor interactor);
    }
}
