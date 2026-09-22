using System;
using Drift.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.UI
{
    // "Steuerung": the pause menu's settings for the tilt steering - switch it on, set the middle, sensitivity
    // and rest zone, with a live picture of what the phone is reporting right now. A plain builder/controller
    // like HelpScreen, so SessionScreens only has to place it into its canvas and open it.
    public sealed class TiltSettingsScreen
    {
        const float PanelWidth = 920f, PanelHeight = 1708f, ContentWidth = 760f;
        const float DialSize = 220f, DotSize = 44f;
        // Everything below the scheme block sits this far further down than it did before it was added.
        const float SchemeBlock = 228f;
        public static readonly Vector2 PanelSize = new Vector2(PanelWidth, PanelHeight);

        GameObject _screen;
        TiltSteering _tilt;
        UiToggle _toggle;
        UiToggle _scheme;
        Text _schemeHint;
        Slider _sensitivity, _deadZone;
        Text _sensText, _deadText, _status, _hint;
        RectTransform _dot, _deadRing;
        Button _calibrate;
        Action _onClose;

        public bool IsOpen => _screen != null && _screen.activeSelf;
        public GameObject Root => _screen;

        public void Build(RectTransform root, TiltSteering tilt, Action onClose)
        {
            _tilt = tilt;
            _onClose = onClose;

            var scrim = UiStyle.Scrim(root, "TiltSettingsScreen", UiStyle.Dim);
            _screen = scrim.gameObject;
            var panel = UiStyle.Panel(scrim, "Panel", PanelSize, true).Center(Vector2.zero, PanelSize);
            UiStyle.FadeIn(_screen, panel);

            var title = UiStyle.Label(panel, "Steuerung", UiStyle.Heading, UiStyle.Sand, TextAnchor.MiddleCenter, true);
            title.rectTransform.TopCenter(new Vector2(0f, -36f), new Vector2(700f, 80f));
            var close = UiStyle.IconButton(panel, "CloseTilt", 88f, UiIcon.Close, Close);
            ((RectTransform)close.transform).Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -32f), new Vector2(88f, 88f));
            UiStyle.Divider(panel, ContentWidth).TopCenter(new Vector2(0f, -128f), new Vector2(ContentWidth, 3f));

            var toggleSize = new Vector2(ContentWidth, 104f);
            // Which scheme steers at all - the tilt below is only one way of naming a direction.
            _scheme = UiStyle.Toggle(panel, "SchemeToggle", "Richtungssteuerung", toggleSize, SteerSettings.Direct, OnScheme);
            ((RectTransform)_scheme.transform).TopCenter(new Vector2(0f, -166f), toggleSize);
            _schemeHint = UiStyle.Label(panel, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            _schemeHint.rectTransform.TopCenter(new Vector2(0f, -282f), new Vector2(ContentWidth, 84f));
            _schemeHint.lineSpacing = UiStyle.Lines(1.2f);
            UiStyle.Divider(panel, ContentWidth).TopCenter(new Vector2(0f, -372f), new Vector2(ContentWidth, 3f));

            _toggle = UiStyle.Toggle(panel, "TiltToggle", "Neigungssteuerung", toggleSize, false, OnToggle);
            ((RectTransform)_toggle.transform).TopCenter(new Vector2(0f, -170f - SchemeBlock), toggleSize);

            _hint = UiStyle.Label(panel, "Handy so halten wie jetzt = stehen bleiben. Kippen lässt die Insel in diese Richtung treiben – je schiefer, desto schneller.",
                UiStyle.Caption, UiStyle.Muted, TextAnchor.UpperCenter);
            _hint.rectTransform.TopCenter(new Vector2(0f, -294f - SchemeBlock), new Vector2(ContentWidth, 84f));
            _hint.lineSpacing = UiStyle.Lines(1.2f);

            _calibrate = UiStyle.PrimaryButton(panel, "Calibrate", "Mitte setzen", new Vector2(ContentWidth, UiStyle.ButtonHeight), OnCalibrate);
            ((RectTransform)_calibrate.transform).TopCenter(new Vector2(0f, -392f - SchemeBlock), new Vector2(ContentWidth, UiStyle.ButtonHeight));

            var rowSize = new Vector2(ContentWidth, UiSlider.RowHeight);
            float sliderY = -566f - SchemeBlock;
            _sensitivity = UiSlider.Row(panel, "Sensitivity", "Empfindlichkeit", rowSize, 0.5f, FormatSensitivity, OnSensitivity, out _sensText);
            ((RectTransform)_sensitivity.transform.parent).TopCenter(new Vector2(0f, sliderY), rowSize);
            _deadZone = UiSlider.Row(panel, "DeadZone", "Ruhezone", rowSize, 0.18f, FormatDeadZone, OnDeadZone, out _deadText);
            ((RectTransform)_deadZone.transform.parent).TopCenter(new Vector2(0f, sliderY - UiSlider.RowHeight - UiStyle.Gap), rowSize);

            BuildDial(panel, sliderY - 2f * (UiSlider.RowHeight + UiStyle.Gap) - 20f);

            _status = UiStyle.Label(panel, "", UiStyle.Caption, UiStyle.Coral, TextAnchor.UpperCenter);
            _status.rectTransform.TopCenter(new Vector2(0f, -1196f - SchemeBlock), new Vector2(ContentWidth, 80f));
            _status.lineSpacing = UiStyle.Lines(1.2f);

            var back = UiStyle.SecondaryButton(panel, "Back", "Zurück", new Vector2(ContentWidth, UiStyle.ButtonHeight), Close);
            ((RectTransform)back.transform).TopCenter(new Vector2(0f, -1296f - SchemeBlock), new Vector2(ContentWidth, UiStyle.ButtonHeight));
            UiStyle.ButtonIcon(back, UiIcon.Back);

            RefreshSchemeHint();
            _screen.SetActive(false);
        }

        // The dial is the honest picture of the chain: the ring is the full deflection, the inner ring the rest
        // zone, the dot where the phone is being held right now.
        void BuildDial(RectTransform panel, float y)
        {
            var dial = UiStyle.Rect(panel, "Dial").TopCenter(new Vector2(0f, y), new Vector2(DialSize, DialSize));
            UiStyle.Shape(dial, "Well", UiSprites.Circle, UiStyle.Track).rectTransform.Stretch();
            UiStyle.Shape(dial, "Rim", UiSprites.CircleRing, UiStyle.Ghost).rectTransform.Stretch();
            _deadRing = UiStyle.Shape(dial, "DeadZone", UiSprites.CircleRing, UiStyle.WithAlpha(UiStyle.Sand, 0.5f)).rectTransform;
            _deadRing.Center(Vector2.zero, new Vector2(DialSize, DialSize) * 0.4f);
            UiStyle.Shape(dial, "Cross", UiSprites.PillOf(3f), UiStyle.Line).rectTransform.Center(Vector2.zero, new Vector2(DialSize * 0.8f, 3f));
            UiStyle.Shape(dial, "CrossV", UiSprites.PillOf(3f), UiStyle.Line).rectTransform.Center(Vector2.zero, new Vector2(3f, DialSize * 0.8f));
            _dot = UiStyle.Shape(dial, "Dot", UiSprites.Circle, UiStyle.Mint).rectTransform;
            _dot.Center(Vector2.zero, new Vector2(DotSize, DotSize));
        }

        static string FormatSensitivity(float v)
        {
            var s = TiltSettings.Default;
            s.sensitivity = v;
            return Mathf.RoundToInt(s.FullTiltDeg) + "°";
        }

        string FormatDeadZone(float v)
        {
            var s = _tilt != null ? _tilt.settings : TiltSettings.Default;
            s.deadZone = v;
            return s.DeadZoneDeg.ToString("0.0") + "°";
        }

        public void Open()
        {
            if (_screen == null) return;
            _screen.SetActive(true);
            if (_scheme != null) _scheme.Set(SteerSettings.Direct, false);
            RefreshSchemeHint();
            if (_tilt != null)
            {
                _tilt.previewing = true;
                _toggle.Set(_tilt.On, false);
                _sensitivity.SetValueWithoutNotify(_tilt.settings.sensitivity);
                _deadZone.SetValueWithoutNotify(_tilt.settings.deadZone);
                if (_sensText != null) _sensText.text = FormatSensitivity(_tilt.settings.sensitivity);
                if (_deadText != null) _deadText.text = FormatDeadZone(_tilt.settings.deadZone);
            }
            Tick();
        }

        public void Hide()
        {
            if (_screen == null || !_screen.activeSelf) return;
            if (_tilt != null) _tilt.previewing = false;
            _screen.SetActive(false);
        }

        void Close()
        {
            Hide();
            _onClose?.Invoke();
        }

        void OnScheme()
        {
            SteerSettings.Direct = !SteerSettings.Direct;
            if (_scheme != null) _scheme.Set(SteerSettings.Direct);
            RefreshSchemeHint();
        }

        void RefreshSchemeHint()
        {
            if (_schemeHint == null) return;
            string text = SteerSettings.Direct
                ? "Die Insel treibt dorthin, wo du hindrückst oder kippst – sie dreht sich nicht, Norden bleibt oben."
                : "Alte Steuerung: A und D drehen die Insel, W gibt Gas, die Kamera folgt ihr.";
            if (_schemeHint.text != text) _schemeHint.text = text;
        }

        void OnToggle()
        {
            if (_tilt == null) return;
            _tilt.On = !_tilt.On;
            _toggle.Set(_tilt.On);
            Tick();
        }

        void OnCalibrate()
        {
            if (_tilt == null) return;
            _tilt.Calibrate();
            Tick();
        }

        void OnSensitivity(float v)
        {
            if (_tilt == null) return;
            _tilt.settings.sensitivity = v;
            _tilt.SaveTuning();
            if (_deadText != null) _deadText.text = FormatDeadZone(_tilt.settings.deadZone);
        }

        void OnDeadZone(float v)
        {
            if (_tilt == null) return;
            _tilt.settings.deadZone = v;
            _tilt.SaveTuning();
        }

        // Called every frame while open: moves the dot and keeps the status line honest.
        public void Tick()
        {
            if (!IsOpen || _tilt == null) return;
            _tilt.previewing = true;
            float full = Mathf.Max(1f, _tilt.settings.FullTiltDeg);
            Vector2 tilt = _tilt.TiltDegrees / full;
            if (tilt.magnitude > 1f) tilt = tilt.normalized;
            float reach = (DialSize - DotSize) * 0.5f;
            if (_dot != null)
            {
                _dot.anchoredPosition = tilt * reach;
                var img = _dot.GetComponent<Image>();
                if (img != null) img.color = _tilt.Direction.sqrMagnitude > 1e-6f ? UiStyle.Mint : UiStyle.Faint;
            }
            if (_deadRing != null)
            {
                float d = Mathf.Clamp(_tilt.settings.deadZone, 0f, 0.9f);
                float size = Mathf.Max(DotSize * 0.8f, DialSize * d + DotSize * 0.5f);
                _deadRing.sizeDelta = new Vector2(size, size);
            }
            if (_status == null) return;
            string text = "";
            if (!_tilt.Available) text = "Dieses Gerät meldet keinen Neigungssensor.";
            else if (!_tilt.On) text = "";
            else if (_tilt.NeutralTooSteep) text = "Die Mitte steht sehr steil. Halte das Handy flacher und setze die Mitte neu, sonst fehlt links/rechts.";
            if (_status.text != text) _status.text = text;
        }
    }
}
