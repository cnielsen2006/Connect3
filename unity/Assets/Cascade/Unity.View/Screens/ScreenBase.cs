using System;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>A screen binder that wants to know when its page is shown or hidden (<see cref="UiRoot"/> calls these).</summary>
    public interface IScreenView
    {
        /// <summary>The page was asked to show (before its fade-in).</summary>
        void OnShown();

        /// <summary>The page was hidden (after its fade-out).</summary>
        void OnHidden();
    }

    /// <summary>Small helpers every binder uses.</summary>
    public static class Ui
    {
        /// <summary>The named element of the right type, or a clear error naming the UXML that lacks it.</summary>
        public static T Get<T>(VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException("UXML has no " + typeof(T).Name + " named '" + name + "'");

        public static void SetDisplay(VisualElement e, bool visible) =>
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Sets the text only when it changed (cheap to call every frame the view-model raises Changed).</summary>
        public static void SetText(Label l, string text)
        {
            if (l.text != text) l.text = text;
        }

        public static void SetText(Button b, string text)
        {
            if (b.text != text) b.text = text;
        }
    }
}
