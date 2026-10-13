using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace JewelryMiniFactory.Economy.Tests
{
    public sealed class GameControllerFocusTests
    {
        [Test, Category("UnityRuntime")]
        public void EditorInspectorFocus_DoesNotPauseGameOrLatchLostFocus()
        {
            // asmdef tests cannot reference predefined Assembly-CSharp directly.
            var controllerType = Type.GetType("JewelryMiniFactory.Gameplay.GameController, Assembly-CSharp");
            Assert.That(controllerType, Is.Not.Null);
            var root = new GameObject("GameController focus regression");
            try
            {
                var controller = root.AddComponent(controllerType);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var paused = controllerType.GetField("applicationPaused", flags);
                var lostFocus = controllerType.GetField("focusLost", flags);
                var focusCallback = controllerType.GetMethod("OnApplicationFocus", flags);
                Assert.That(paused.GetValue(controller), Is.False);
                // Reproduce Game View -> Inspector/dialog -> Game View, including a repeated event.
                focusCallback.Invoke(controller, new object[] { false });
                Assert.That(paused.GetValue(controller), Is.False);
                Assert.That(lostFocus.GetValue(controller), Is.False);
                focusCallback.Invoke(controller, new object[] { false });
                focusCallback.Invoke(controller, new object[] { true });
                Assert.That(paused.GetValue(controller), Is.False);
                Assert.That(lostFocus.GetValue(controller), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
