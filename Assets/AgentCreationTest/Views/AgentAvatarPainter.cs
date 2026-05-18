using AgentCreationTest.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace AgentCreationTest.Views
{
    // Re-paints the static avatar tree (head/hair/eyes/mouth/top/bottom/shoes)
    // when the wardrobe selection changes. The tree itself is defined in UXML —
    // this only pushes colours/shape modifiers to the existing elements.
    public sealed class AgentAvatarPainter
    {
        private readonly VisualElement _stage;
        private readonly VisualElement _head;
        private readonly VisualElement _hair;
        private readonly VisualElement _bunExtra;
        private readonly VisualElement _eyeLeft;
        private readonly VisualElement _eyeRight;
        private readonly VisualElement _mouth;
        private readonly VisualElement _top;
        private readonly VisualElement _bottom;
        private readonly VisualElement _shoeLeft;
        private readonly VisualElement _shoeRight;

        private string _currentHairShape;

        public AgentAvatarPainter(VisualElement root)
        {
            _stage     = root.Q<VisualElement>("avatar-stage");
            _head      = root.Q<VisualElement>("avatar-head");
            _hair      = root.Q<VisualElement>("avatar-hair");
            _bunExtra  = root.Q<VisualElement>("avatar-bun-extra");
            _eyeLeft   = root.Q<VisualElement>("avatar-eye-left");
            _eyeRight  = root.Q<VisualElement>("avatar-eye-right");
            _mouth     = root.Q<VisualElement>("avatar-mouth");
            _top       = root.Q<VisualElement>("avatar-top");
            _bottom    = root.Q<VisualElement>("avatar-bottom");
            _shoeLeft  = root.Q<VisualElement>("avatar-shoe-left");
            _shoeRight = root.Q<VisualElement>("avatar-shoe-right");
        }

        public void Apply(Wardrobe w)
        {
            if (_head == null) return;

            // Wardrobe.None (-1) is a valid value — every slot except Eyes/Mouth
            // exposes a None cell in the wizard. Indexing the palette with -1
            // throws IndexOutOfRangeException, which used to silently kill the
            // PropertyChanged handler and freeze the option grid. Each lookup
            // bails on negative / out-of-range indices and the corresponding
            // visual element is hidden so the avatar still reads as "no item".
            ApplyColor(_head,      AgentPalette.SkinColors,   w.Skin);
            ApplyColor(_top,       AgentPalette.TopColors,    w.Top);
            ApplyColor(_bottom,    AgentPalette.BottomColors, w.Bottom);
            ApplyColor(_shoeLeft,  AgentPalette.ShoesColors,  w.Shoes);
            ApplyColor(_shoeRight, AgentPalette.ShoesColors,  w.Shoes);

            var hairColor = ColorAt(AgentPalette.HairColors, w.Hair);
            if (hairColor != null)
            {
                SetBg(_hair, hairColor);
                SetBg(_bunExtra, hairColor);
            }
            else
            {
                Hide(_hair);
                Hide(_bunExtra);
            }

            if (w.Hair >= 0 && w.Hair < AgentPalette.HairShapes.Length)
                ApplyHairShape(AgentPalette.HairShapes[w.Hair]);
            if (w.Eyes >= 0 && w.Eyes < AgentPalette.EyeStyles.Length)
                ApplyEyeStyle(AgentPalette.EyeStyles[w.Eyes]);
            if (w.Mouth >= 0 && w.Mouth < AgentPalette.MouthStyles.Length)
                ApplyMouthStyle(AgentPalette.MouthStyles[w.Mouth]);
        }

        private static void ApplyColor(VisualElement element, string[] palette, int index)
        {
            if (element == null) return;
            var hex = ColorAt(palette, index);
            if (hex != null)
            {
                SetBg(element, hex);
                element.style.visibility = Visibility.Visible;
            }
            else
            {
                Hide(element);
            }
        }

        private static string ColorAt(string[] palette, int index)
        {
            if (palette == null) return null;
            if (index < 0 || index >= palette.Length) return null;
            return palette[index];
        }

        private static void Hide(VisualElement element)
        {
            if (element == null) return;
            element.style.visibility = Visibility.Hidden;
        }

        private void ApplyHairShape(string shape)
        {
            if (_hair == null || _stage == null) return;
            if (_currentHairShape != null)
            {
                _hair.RemoveFromClassList("avatar-hair--" + _currentHairShape);
                _stage.RemoveFromClassList("avatar-hair-shape--" + _currentHairShape);
            }
            _currentHairShape = shape;
            _hair.AddToClassList("avatar-hair--" + shape);
            _stage.AddToClassList("avatar-hair-shape--" + shape);
        }

        // 9 eye styles, mirroring renderEyes() in opendesk-onboarding.jsx.
        private void ApplyEyeStyle(string style)
        {
            if (_eyeLeft == null || _eyeRight == null) return;
            ResetEye(_eyeLeft);
            ResetEye(_eyeRight);

            switch (style)
            {
                case "dot":
                    SizeEye(_eyeLeft, 4, 5, 2); SizeEye(_eyeRight, 4, 5, 2);
                    break;
                case "line":
                    SizeEye(_eyeLeft, 6, 1.5f, 0); SizeEye(_eyeRight, 6, 1.5f, 0);
                    break;
                case "curve":
                    AsCurve(_eyeLeft); AsCurve(_eyeRight);
                    break;
                case "wide":
                    SizeEye(_eyeLeft, 6, 7, 4); SizeEye(_eyeRight, 6, 7, 4);
                    break;
                case "wink":
                    SizeEye(_eyeLeft, 6, 1.5f, 0); SizeEye(_eyeRight, 4, 5, 2);
                    break;
                case "closed":
                    AsClosed(_eyeLeft); AsClosed(_eyeRight);
                    break;
                case "sparkle":
                    SizeEye(_eyeLeft, 5, 6, 3); SizeEye(_eyeRight, 5, 6, 3);
                    break;
                case "tired":
                    AsTired(_eyeLeft, 8f); AsTired(_eyeRight, -8f);
                    break;
                case "focused":
                    SizeEye(_eyeLeft, 3, 5, 2); SizeEye(_eyeRight, 3, 5, 2);
                    break;
            }
        }

        // 9 mouth styles, mirroring renderMouth().
        private void ApplyMouthStyle(string style)
        {
            if (_mouth == null) return;
            ResetMouth(_mouth);

            // Common: position centred, top:44px (frown overrides to 48px).
            _mouth.style.top = 44f;

            switch (style)
            {
                case "smile":
                    MouthCurveDown(_mouth, 14, 6, 1.5f, 14);
                    break;
                case "open":
                    MouthBlock(_mouth, 8, 8, 4);
                    break;
                case "flat":
                    MouthBar(_mouth, 12, 1.5f);
                    break;
                case "smirk":
                    MouthCurveDown(_mouth, 12, 5, 1.5f, 12);
                    _mouth.style.translate = new StyleTranslate(new Translate(new Length(-30, LengthUnit.Percent), new Length(0f), 0f));
                    break;
                case "o":
                    MouthBlock(_mouth, 6, 8, 4);
                    break;
                case "grin":
                    MouthCurveDown(_mouth, 18, 8, 2f, 18);
                    break;
                case "small":
                    MouthCurveDown(_mouth, 6, 3, 1.5f, 6);
                    break;
                case "frown":
                    _mouth.style.top = 48f;
                    MouthCurveUp(_mouth, 12, 5, 1.5f, 12);
                    break;
                case "tongue":
                    MouthCurveDown(_mouth, 14, 7, 1.5f, 14);
                    _mouth.style.backgroundColor = ParseColor("#E89B8B");
                    break;
            }
        }

        // ─── helpers ───────────────────────────────────────────

        private static void SetBg(VisualElement el, string hex)
        {
            if (el == null) return;
            el.style.backgroundColor = ParseColor(hex);
        }

        private static Color ParseColor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }

        private static void ResetEye(VisualElement eye)
        {
            eye.style.width = 0;
            eye.style.height = 0;
            eye.style.borderTopLeftRadius = 0;
            eye.style.borderTopRightRadius = 0;
            eye.style.borderBottomLeftRadius = 0;
            eye.style.borderBottomRightRadius = 0;
            eye.style.backgroundColor = ParseColor("#2A201A");
            eye.style.borderTopWidth = 0;
            eye.style.borderBottomWidth = 0;
            eye.style.borderTopColor = ParseColor("#2A201A");
            eye.style.borderBottomColor = ParseColor("#2A201A");
            eye.style.rotate = new StyleRotate(new Rotate(new Angle(0, AngleUnit.Degree)));
            eye.style.top = 30f;
        }

        private static void SizeEye(VisualElement eye, float w, float h, float radius)
        {
            eye.style.width = w;
            eye.style.height = h;
            ApplyRadius(eye, radius);
        }

        private static void AsCurve(VisualElement eye)
        {
            eye.style.width = 7;
            eye.style.height = 4;
            eye.style.backgroundColor = new Color(0, 0, 0, 0);
            eye.style.borderTopWidth = 1.5f;
            eye.style.borderTopLeftRadius = 7f;
            eye.style.borderTopRightRadius = 7f;
        }

        private static void AsClosed(VisualElement eye)
        {
            eye.style.width = 7;
            eye.style.height = 3;
            eye.style.backgroundColor = new Color(0, 0, 0, 0);
            eye.style.borderBottomWidth = 1.5f;
            eye.style.borderBottomLeftRadius = 7f;
            eye.style.borderBottomRightRadius = 7f;
        }

        private static void AsTired(VisualElement eye, float angleDeg)
        {
            eye.style.width = 7;
            eye.style.height = 1.5f;
            eye.style.top = 32f;
            eye.style.rotate = new StyleRotate(new Rotate(new Angle(angleDeg, AngleUnit.Degree)));
        }

        private static void ResetMouth(VisualElement mouth)
        {
            mouth.style.width = 0;
            mouth.style.height = 0;
            mouth.style.backgroundColor = new Color(0, 0, 0, 0);
            mouth.style.borderTopWidth = 0;
            mouth.style.borderBottomWidth = 0;
            mouth.style.borderTopColor = ParseColor("#2A201A");
            mouth.style.borderBottomColor = ParseColor("#2A201A");
            mouth.style.borderTopLeftRadius = 0;
            mouth.style.borderTopRightRadius = 0;
            mouth.style.borderBottomLeftRadius = 0;
            mouth.style.borderBottomRightRadius = 0;
            // Center horizontally regardless of width changes.
            mouth.style.left = new Length(50, LengthUnit.Percent);
            mouth.style.translate = new StyleTranslate(new Translate(new Length(-50, LengthUnit.Percent), new Length(0f), 0f));
        }

        private static void MouthBar(VisualElement mouth, float w, float h)
        {
            mouth.style.width = w;
            mouth.style.height = h;
            mouth.style.marginLeft = -w / 2f;
            mouth.style.left = new Length(50, LengthUnit.Percent);
            mouth.style.translate = new StyleTranslate(new Translate(new Length(0f), new Length(0f), 0f));
            mouth.style.backgroundColor = ParseColor("#2A201A");
        }

        private static void MouthBlock(VisualElement mouth, float w, float h, float radius)
        {
            mouth.style.width = w;
            mouth.style.height = h;
            mouth.style.backgroundColor = ParseColor("#2A201A");
            mouth.style.marginLeft = -w / 2f;
            mouth.style.left = new Length(50, LengthUnit.Percent);
            mouth.style.translate = new StyleTranslate(new Translate(new Length(0f), new Length(0f), 0f));
            ApplyRadius(mouth, radius);
        }

        private static void MouthCurveDown(VisualElement mouth, float w, float h, float strokeWidth, float radius)
        {
            mouth.style.width = w;
            mouth.style.height = h;
            mouth.style.borderBottomWidth = strokeWidth;
            mouth.style.borderBottomLeftRadius = radius;
            mouth.style.borderBottomRightRadius = radius;
            mouth.style.marginLeft = -w / 2f;
            mouth.style.left = new Length(50, LengthUnit.Percent);
            mouth.style.translate = new StyleTranslate(new Translate(new Length(0f), new Length(0f), 0f));
        }

        private static void MouthCurveUp(VisualElement mouth, float w, float h, float strokeWidth, float radius)
        {
            mouth.style.width = w;
            mouth.style.height = h;
            mouth.style.borderTopWidth = strokeWidth;
            mouth.style.borderTopLeftRadius = radius;
            mouth.style.borderTopRightRadius = radius;
            mouth.style.marginLeft = -w / 2f;
            mouth.style.left = new Length(50, LengthUnit.Percent);
            mouth.style.translate = new StyleTranslate(new Translate(new Length(0f), new Length(0f), 0f));
        }

        private static void ApplyRadius(VisualElement el, float radius)
        {
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
        }
    }
}
