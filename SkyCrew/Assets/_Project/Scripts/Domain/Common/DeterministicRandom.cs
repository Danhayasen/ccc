using System;

namespace SkyCrew.Domain.Common
{
    /// <summary>Fonte de aleatoriedade injetavel (permite testes reproduziveis).</summary>
    public interface IRandom
    {
        /// <summary>Valor em [0, 1).</summary>
        double NextDouble();

        /// <summary>Inteiro em [minInclusive, maxExclusive).</summary>
        int Range(int minInclusive, int maxExclusive);
    }

    /// <summary>
    /// Gerador SplitMix64: rapido, sem alocacao e identico em qualquer plataforma
    /// (System.Random nao garante a mesma sequencia entre runtimes).
    /// </summary>
    public sealed class DeterministicRandom : IRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            Seed = seed;
            _state = seed;
        }

        public ulong Seed { get; private set; }

        private ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / (1UL << 53));
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentException("maxExclusive deve ser maior que minInclusive");
            }

            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }
    }

    public static class MathUtil
    {
        public static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
