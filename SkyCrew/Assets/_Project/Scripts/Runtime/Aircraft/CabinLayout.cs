using UnityEngine;

namespace SkyCrew.Aircraft
{
    /// <summary>
    /// Medidas da secao de cabine do prototipo (metros, Y para cima, Z = frente/nariz).
    /// Centralizadas aqui para o construtor, a navegacao e os testes usarem os mesmos numeros.
    /// </summary>
    public static class CabinLayout
    {
        public const float HalfWidth = 1.9f;      // parede interna
        public const float CeilingHeight = 2.25f;
        public const float FrontZ = 5.2f;          // parede da cabine de comando
        public const float RearZ = -5.2f;          // parede traseira
        public const int Rows = 6;
        public const float FirstRowZ = 2.0f;
        public const float RowPitch = 0.9f;
        public const float SeatWidth = 0.48f;
        public const float SeatDepth = 0.55f;
        public const float SeatHeight = 0.45f;
        public const float AisleHalfWidth = 0.31f;

        // Cozinha (frente) e banheiro (fundo)
        public const float GalleyStartZ = 3.0f;
        public const float LavatoryStartZ = -3.4f;

        public static readonly string[] SeatLetters = { "A", "B", "C", "D", "E", "F" };

        public static float RowZ(int row) => FirstRowZ - (row - 1) * RowPitch;

        /// <summary>X do centro da poltrona: A..C a esquerda (x negativo), D..F a direita.</summary>
        public static float SeatX(int letterIndex)
        {
            float first = AisleHalfWidth + SeatWidth * 0.5f;
            int side = letterIndex < 3 ? -1 : 1;
            int fromAisle = letterIndex < 3 ? 2 - letterIndex : letterIndex - 3;
            return side * (first + fromAisle * SeatWidth);
        }

        public static string SeatId(int row, int letterIndex) => row + SeatLetters[letterIndex];

        public static bool TryParseSeat(string seatId, out int row, out int letterIndex)
        {
            row = 0;
            letterIndex = -1;
            if (string.IsNullOrEmpty(seatId) || seatId.Length < 2)
            {
                return false;
            }

            string letter = seatId.Substring(seatId.Length - 1);
            letterIndex = System.Array.IndexOf(SeatLetters, letter);
            return letterIndex >= 0 && int.TryParse(seatId.Substring(0, seatId.Length - 1), out row)
                   && row >= 1 && row <= Rows;
        }

        /// <summary>
        /// Onde fica a raiz (pes) do passageiro sentado: no piso, no centro da poltrona.
        /// A animacao "Sentado" abaixa o quadril (0,56 m -> 0,46 m) ate o estofado (0,47 m).
        /// </summary>
        public static Vector3 SeatPosition(int row, int letterIndex)
            => new Vector3(SeatX(letterIndex), 0f, RowZ(row) - 0.05f);

        /// <summary>Ponto do corredor ao lado da fileira (destino de navegacao).</summary>
        public static Vector3 AislePointForRow(int row) => new Vector3(0f, 0f, RowZ(row));

        public static Vector3 BoardingDoor => new Vector3(0f, 0f, GalleyStartZ - 0.2f);
        public static Vector3 PlayerSpawn => new Vector3(0f, 0f, 4.2f);
        public static Vector3 LavatoryPoint => new Vector3(0f, 0f, -4.3f);
        public static Vector3 ForgottenSuitcase => new Vector3(0.05f, 0.35f, 0.35f);

        public static Bounds NavBounds => new Bounds(new Vector3(0f, 1f, 0f), new Vector3(HalfWidth * 2f + 1f, 3f, FrontZ - RearZ + 1f));
    }
}
