using System.Collections.Generic;
using Drift.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Tests
{
    // The invisible steering touch (2026-10-02, owner: "ganz unsichtbar, ganzer Bildschirm, kein Schalter"): a finger
    // anywhere off the buttons steers by its drag from where it went down, nothing is drawn, and a finger that never
    // leaves the dead zone stays a tap.
    public class TouchControlsTests
    {
        readonly List<GameObject> _objects = new();
        TouchControls _touch;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("TouchControlsTest") { hideFlags = HideFlags.DontSave };
            _objects.Add(go);
            _touch = go.AddComponent<TouchControls>();
        }

        [TearDown]
        public void TearDown()
        {
            TouchControls.UiHitOverride = null;
            foreach (var go in _objects) if (go != null) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        static TouchControls.TouchPointer Finger(Vector2 pos, bool began, int id = 3) => new TouchControls.TouchPointer(id, pos, began);

        static Vector2 RightHalf => new Vector2(Screen.width * 0.8f, Screen.height * 0.5f);

        [Test]
        public void TouchInTheRightHalf_Steers()
        {
            Vector2 start = RightHalf;
            _touch.Feed(Finger(start, true));
            Assert.IsTrue(_touch.StickActive, "the right half is no longer off limits");
            Assert.AreEqual(Vector2.zero, _touch.Move);

            _touch.Feed(Finger(start + new Vector2(-_touch.RadiusPixels, 0f), false));
            Assert.IsTrue(_touch.StickDeflected);
            Assert.AreEqual(-1f, _touch.Move.x, 1e-4f, "full deflection to the left, measured from the touch-down point");
            Assert.AreEqual(0f, _touch.Move.y, 1e-4f);

            _touch.Feed(Finger(start + new Vector2(0f, _touch.RadiusPixels * 3f), false));
            Assert.AreEqual(1f, _touch.Move.magnitude, 1e-4f, "clamped at the radius");
            Assert.Greater(_touch.Move.y, 0.99f);

            _touch.Feed();
            Assert.IsFalse(_touch.StickActive);
            Assert.AreEqual(Vector2.zero, _touch.Move);
        }

        [Test]
        public void HalfwayDrag_KeepsTheDeadZoneMaths()
        {
            Vector2 start = new Vector2(Screen.width * 0.3f, Screen.height * 0.2f);
            _touch.Feed(Finger(start, true));
            _touch.Feed(Finger(start + new Vector2(0f, -0.5f * _touch.RadiusPixels), false));
            float expected = (0.5f - _touch.deadZone) / (1f - _touch.deadZone);
            Assert.AreEqual(-expected, _touch.Move.y, 1e-4f);
            Assert.AreEqual(0f, _touch.Move.x, 1e-4f);
        }

        [Test]
        public void TapInsideTheDeadZone_StaysATap()
        {
            Vector2 start = RightHalf;
            _touch.Feed(Finger(start, true));
            _touch.Feed(Finger(start + new Vector2(0.6f, 0.6f) * (_touch.deadZone * _touch.RadiusPixels), false));
            Assert.IsTrue(_touch.IsPointerOnStick(3), "the stick holds the finger ...");
            Assert.IsFalse(_touch.StickDeflected, "... but it never left the dead zone, so WatchTools reads a tap");
            Assert.AreEqual(Vector2.zero, _touch.Move, "the island does not move under a tap");
            _touch.Feed();
            Assert.IsFalse(_touch.StickActive);
        }

        [Test]
        public void TwoFingers_PinchAndHoldTheIsland()
        {
            Vector2 a = new Vector2(Screen.width * 0.3f, Screen.height * 0.5f), b = new Vector2(Screen.width * 0.7f, Screen.height * 0.5f);
            _touch.Feed(Finger(a, true, 1));
            _touch.Feed(Finger(a + new Vector2(_touch.RadiusPixels, 0f), false, 1), Finger(b, true, 2));
            Assert.IsTrue(_touch.IsPinching);
            Assert.AreEqual(Vector2.zero, _touch.Move, "no steering while two fingers zoom");
            _touch.Feed(Finger(a + new Vector2(_touch.RadiusPixels, 0f), false, 1), Finger(b + new Vector2(Screen.width * 0.1f, 0f), false, 2));
            Assert.Less(_touch.ConsumePinchFactor(), 1f, "fingers apart zoom in");
        }

        [Test]
        public void StickOff_NoFingerSteers()
        {
            _touch.stickEnabled = false;
            _touch.Feed(Finger(RightHalf, true));
            Assert.IsFalse(_touch.StickActive);
        }

        [Test]
        public void TouchOnAButton_DoesNotSteer()
        {
            // A button in the lower right; the real raycast (EventSystem.RaycastAll) needs a rendered canvas.
            var button = new Rect(Screen.width * 0.5f, 0f, Screen.width * 0.5f, Screen.height * 0.6f);
            TouchControls.UiHitOverride = button.Contains;
            Assert.IsTrue(TouchControls.OverGraphic(RightHalf));

            _touch.Feed(Finger(RightHalf, true));
            Assert.IsFalse(_touch.StickActive, "a finger that went down on a button never steers");
            _touch.Feed(Finger(RightHalf + new Vector2(-_touch.RadiusPixels, 0f), false));
            Assert.IsFalse(_touch.StickActive);
            Assert.AreEqual(Vector2.zero, _touch.Move, "not even once it slides off the button");
            _touch.Feed();

            Vector2 beside = new Vector2(Screen.width * 0.8f, Screen.height * 0.8f);
            _touch.Feed(Finger(beside, true, 4));
            Assert.IsTrue(_touch.StickActive, "above the button the finger steers");
            _touch.Feed(Finger(beside + new Vector2(0f, -_touch.RadiusPixels), false, 4));
            Assert.AreEqual(-1f, _touch.Move.y, 1e-4f, "and keeps steering when it slides over the button");
        }

        [Test]
        public void NothingIsDrawn()
        {
            Assert.AreEqual(0, _touch.GetComponentsInChildren<Graphic>(true).Length);
            Assert.AreEqual(0, _touch.GetComponentsInChildren<Canvas>(true).Length);
            Assert.AreEqual(0, _touch.transform.childCount);

            // An Editor session from before still carries the old stick canvas: enabling clears it.
            _touch.enabled = false;
            var legacy = new GameObject("TouchCanvas", typeof(Canvas));
            legacy.transform.SetParent(_touch.transform, false);
            new GameObject("StickKnob", typeof(RectTransform), typeof(Image)).transform.SetParent(legacy.transform, false);
            _touch.enabled = true;
            Assert.AreEqual(0, _touch.transform.childCount);

            _touch.Feed(Finger(RightHalf, true));
            _touch.Feed(Finger(RightHalf + Vector2.one * _touch.RadiusPixels, false));
            Assert.IsTrue(_touch.StickDeflected);
            Assert.AreEqual(0, _touch.transform.childCount, "steering creates nothing either");
        }
    }
}
