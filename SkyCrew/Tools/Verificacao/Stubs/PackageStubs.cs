// STUBS SOMENTE PARA CHECAGEM DE COMPILACAO FORA DA UNITY.
// Reproduzem apenas as assinaturas usadas pelo projeto, copiadas da API publica dos pacotes:
//   com.unity.inputsystem (1.x)   -> UnityEngine.InputSystem
//   com.unity.test-framework      -> UnityEngine.TestTools
// Nao sao compilados pela Unity (ficam fora de Assets/). Se uma assinatura aqui estiver
// errada, o erro so aparecera no Editor: veja Docs/KNOWN_ISSUES.md.
#pragma warning disable CS1591
using System;
using System.Collections;

namespace UnityEngine.InputSystem
{
    public enum InputActionType
    {
        Value,
        Button,
        PassThrough,
    }

    public sealed class InputAction : IDisposable
    {
        public InputAction(string name = null, InputActionType type = default, string binding = null,
                           string interactions = null, string processors = null, string expectedControlType = null)
        {
        }

        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }
        public TValue ReadValue<TValue>() where TValue : struct => default;
        public bool WasPressedThisFrame() => false;
        public bool IsPressed() => false;
    }

    public static class InputActionSetupExtensions
    {
        public static BindingSyntax AddBinding(this InputAction action, string path, string interactions = null,
                                               string processors = null, string groups = null) => default;

        public static CompositeSyntax AddCompositeBinding(this InputAction action, string composite,
                                                          string interactions = null, string processors = null) => default;

        public struct BindingSyntax
        {
        }

        public struct CompositeSyntax
        {
            public CompositeSyntax With(string name, string binding, string groups = null, string processors = null) => this;
        }
    }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnityTestAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnitySetUpAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnityTearDownAttribute : Attribute
    {
    }
}
