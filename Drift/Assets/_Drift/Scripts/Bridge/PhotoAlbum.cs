using System;
using System.Collections.Generic;
using Drift.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Drift.Bridge
{
    // "Fotoalbum": a paged grid of thumbnails and a full view with delete. A plain builder/controller that
    // WatchTools places into its canvas (like HelpScreen in SessionScreens), so it needs no scene wiring.
    // Textures are loaded one per Tick for the visible page only and destroyed on page change and close.
    public sealed class PhotoAlbum
    {
        public const int Columns = 3, Rows = 4, PerPage = Columns * Rows;
        const float PanelWidth = 960f, PanelHeight = 1680f, Cell = 272f, CellGap = 24f, GridTop = -230f;
        const int SampleCount = 14;

        readonly List<PhotoEntry> _entries = new List<PhotoEntry>();
        readonly RawImage[] _cellImage = new RawImage[PerPage];
        readonly GameObject[] _cell = new GameObject[PerPage];
        readonly Texture2D[] _thumbs = new Texture2D[PerPage];

        GameObject _grid, _view, _empty, _pager, _viewButtons, _confirmButtons;
        RectTransform _root, _photoFrame;
        RawImage _photo;
        Text _count, _pageLabel, _date, _position, _confirmText;
        Button _prev, _next;
        UiPageDots _dots;
        Texture2D _full;
        Action _onClosed;
        string _directory;
        bool _sample;
        int _page, _loadCursor, _viewIndex = -1;
        Vector2 _fittedFor;

        public bool IsOpen => (_grid != null && _grid.activeSelf) || ViewingPhoto;
        public bool ViewingPhoto => _view != null && _view.activeSelf;
        public bool Confirming => _confirmButtons != null && _confirmButtons.activeSelf;
        public int PhotoCount => _entries.Count;
        public int Page => _page;
        public int LoadedThumbnails
        {
            get
            {
                int n = 0;
                foreach (var t in _thumbs) if (t != null) n++;
                return n;
            }
        }

        // ---------------------------------------------------------------- control

        public void Open(string directory)
        {
            if (_grid == null) return;
            _sample = false;
            _directory = directory;
            PhotoLibrary.List(directory, _entries);
            ShowGrid(0);
        }

        // Edit Mode preview: made-up pictures, nothing is read from or written to disk.
        public void OpenSample(bool photoView)
        {
            if (_grid == null) return;
            _sample = true;
            _directory = null;
            _entries.Clear();
            var t = new DateTime(2026, 9, 20, 18, 42, 0);
            for (int i = 0; i < SampleCount; i++)
                _entries.Add(new PhotoEntry { name = PhotoLibrary.FileNameFor(t.AddMinutes(-37 * i)), taken = t.AddMinutes(-37 * i) });
            ShowGrid(0);
            while (_loadCursor < PerPage && LoadNext()) { }
            if (photoView) ShowPhoto(0);
        }

        public void Close()
        {
            bool was = IsOpen;
            ReleaseThumbs();
            ReleaseFull();
            if (_grid != null) _grid.SetActive(false);
            if (_view != null) _view.SetActive(false);
            _viewIndex = -1;
            _entries.Clear();
            if (was) _onClosed?.Invoke();
        }

        // Escape / Android back: confirm -> full view -> grid -> closed.
        public void Back()
        {
            if (Confirming) SetConfirm(false);
            else if (ViewingPhoto) ShowGrid(_page);
            else Close();
        }

        public void Tick()
        {
            if (_grid != null && _grid.activeSelf) LoadNext();
            if (ViewingPhoto && _root != null && _root.rect.size != _fittedFor) FitPhoto();
        }

        // ---------------------------------------------------------------- grid

        void ShowGrid(int page)
        {
            ReleaseFull();
            _viewIndex = -1;
            _view.SetActive(false);
            _grid.SetActive(true);
            _grid.transform.SetAsLastSibling();
            int pages = PhotoLibrary.PageCount(_entries.Count, PerPage);
            _page = Mathf.Clamp(page, 0, pages - 1);
            ReleaseThumbs();
            _loadCursor = 0;
            _count.text = PhotoLibrary.CountLabel(_entries.Count);
            _empty.SetActive(_entries.Count == 0);
            _pager.SetActive(pages > 1);
            _pageLabel.text = "Seite " + (_page + 1) + " von " + pages;
            bool dots = pages <= _dots.Capacity;
            if (_dots.Root.gameObject.activeSelf != dots) _dots.Root.gameObject.SetActive(dots);
            if (_pageLabel.gameObject.activeSelf == dots) _pageLabel.gameObject.SetActive(!dots);
            if (dots) _dots.Set(_page, pages);
            UiStyle.SetInteractable(_prev, _page > 0);
            UiStyle.SetInteractable(_next, _page < pages - 1);
            for (int i = 0; i < PerPage; i++)
            {
                bool on = _page * PerPage + i < _entries.Count;
                if (_cell[i].activeSelf != on) _cell[i].SetActive(on);
                _cellImage[i].texture = null;
                _cellImage[i].color = UiStyle.WithAlpha(UiStyle.Cream, 0f);
            }
        }

        bool LoadNext()
        {
            int index = _page * PerPage + _loadCursor;
            if (_loadCursor >= PerPage || index >= _entries.Count) return false;
            int slot = _loadCursor++;
            var tex = _sample ? SampleTexture(index, 96, 54) : PhotoLibrary.LoadThumbnail(_entries[index]);
            _thumbs[slot] = tex;
            if (tex == null) return true;
            _cellImage[slot].texture = tex;
            _cellImage[slot].uvRect = PhotoLibrary.SquareCrop(tex.width, tex.height);
            _cellImage[slot].color = Color.white;
            return true;
        }

        void ReleaseThumbs()
        {
            for (int i = 0; i < PerPage; i++)
            {
                if (_cellImage[i] != null) _cellImage[i].texture = null;
                PhotoLibrary.Dispose(_thumbs[i]);
                _thumbs[i] = null;
            }
        }

        // ---------------------------------------------------------------- full view

        public void ShowPhoto(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            ReleaseFull();
            _viewIndex = index;
            var e = _entries[index];
            _full = _sample ? SampleTexture(index, 320, 180) : PhotoLibrary.LoadTexture(e.path);
            _photo.texture = _full;
            _photo.color = _full != null ? Color.white : UiStyle.WithAlpha(UiStyle.Cream, 0f);
            _date.text = PhotoLibrary.FormatDate(e.taken);
            _position.text = "Foto " + (index + 1) + " von " + _entries.Count;
            SetConfirm(false);
            _grid.SetActive(false);
            _view.SetActive(true);
            _view.transform.SetAsLastSibling();
            FitPhoto();
        }

        // The picture fills the space between the date line and the buttons at its own aspect.
        void FitPhoto()
        {
            _fittedFor = _root.rect.size;
            Vector2 room = new Vector2(Mathf.Max(200f, _fittedFor.x - 2f * UiStyle.Margin), Mathf.Max(200f, _fittedFor.y - 490f));
            float aspect = _full != null && _full.height > 0 ? _full.width / (float)_full.height : 16f / 9f;
            Vector2 size = room.x / room.y > aspect ? new Vector2(room.y * aspect, room.y) : new Vector2(room.x, room.x / aspect);
            _photoFrame.Center(new Vector2(0f, -15f), size);
        }

        void ReleaseFull()
        {
            if (_photo != null) _photo.texture = null;
            PhotoLibrary.Dispose(_full);
            _full = null;
        }

        void SetConfirm(bool on)
        {
            _viewButtons.SetActive(!on);
            _confirmButtons.SetActive(on);
            _confirmText.text = on ? "Dieses Foto wirklich löschen?" : "";
        }

        void DeleteShown()
        {
            if (_viewIndex < 0 || _viewIndex >= _entries.Count) return;
            if (!_sample) PhotoLibrary.Delete(_entries[_viewIndex]);
            _entries.RemoveAt(_viewIndex);
            ShowGrid(_page);
        }

        // ---------------------------------------------------------------- layout

        public void Build(RectTransform root, Action onClosed)
        {
            _root = root;
            _onClosed = onClosed;
            BuildGrid(root);
            BuildView(root);
            _grid.SetActive(false);
            _view.SetActive(false);
        }

        void BuildGrid(RectTransform root)
        {
            var scrim = UiStyle.Scrim(root, "AlbumScreen", UiStyle.Dim);
            _grid = scrim.gameObject;
            var size = new Vector2(PanelWidth, PanelHeight);
            var panel = UiStyle.Panel(scrim, "Panel", size, true).Center(Vector2.zero, size);
            UiStyle.FadeIn(_grid, panel);

            var title = UiStyle.Label(panel, "Fotoalbum", 96, UiStyle.Sand, TextAnchor.UpperCenter, true);
            title.rectTransform.TopCenter(new Vector2(0f, -30f), new Vector2(860f, 124f));
            _count = UiStyle.Label(panel, "", 34, UiStyle.CreamSoft);
            _count.rectTransform.TopCenter(new Vector2(0f, -160f), new Vector2(860f, 44f));

            float width = Columns * Cell + (Columns - 1) * CellGap, height = Rows * Cell + (Rows - 1) * CellGap;
            var grid = UiStyle.Rect(panel, "Grid").TopCenter(new Vector2(0f, GridTop), new Vector2(width, height));
            for (int i = 0; i < PerPage; i++)
            {
                int slot = i;
                var cell = UiStyle.Rect(grid, "Photo" + i).TopLeft(new Vector2(i % Columns * (Cell + CellGap), -(i / Columns) * (Cell + CellGap)), new Vector2(Cell, Cell));
                _cellImage[i] = UiStyle.Picture(cell, "Thumb", new Vector2(Cell, Cell), out var frame);
                frame.Stretch();
                var mask = frame.Find("Mask").GetComponent<Image>();
                mask.raycastTarget = true;
                var button = cell.gameObject.AddComponent<Button>();
                cell.gameObject.AddComponent<UiPressFeedback>();
                button.targetGraphic = mask;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => ShowPhoto(_page * PerPage + slot));
                _cell[i] = cell.gameObject;
            }

            var empty = UiStyle.Rect(panel, "Empty").TopCenter(new Vector2(0f, GridTop - 300f), new Vector2(760f, 520f));
            _empty = empty.gameObject;
            UiStyle.Icon(empty, "Glyph", UiIcon.Camera, 200f, UiStyle.Faint).rectTransform.TopCenter(new Vector2(0f, -10f), new Vector2(200f, 200f));
            var line1 = UiStyle.Label(empty, "Noch keine Fotos", UiStyle.Subheading, UiStyle.Cream, TextAnchor.UpperCenter, true);
            line1.rectTransform.TopCenter(new Vector2(0f, -230f), new Vector2(760f, 60f));
            var line2 = UiStyle.Label(empty, "Tippe im Spiel auf das Kamera-Symbol und halte fest, was dir auf deiner Insel gefällt. Deine Bilder sammeln sich dann hier.", UiStyle.Body, UiStyle.Muted);
            line2.rectTransform.TopCenter(new Vector2(0f, -306f), new Vector2(720f, 200f));

            var pager = UiStyle.Rect(panel, "Pager").TopCenter(new Vector2(0f, GridTop - height - 18f), new Vector2(width, 88f));
            _pager = pager.gameObject;
            _prev = ArrowButton(pager, "PreviousPage", 0f, 90f, () => ShowGrid(_page - 1));
            _next = ArrowButton(pager, "NextPage", 1f, -90f, () => ShowGrid(_page + 1));
            _pageLabel = UiStyle.Label(pager, "", 32, UiStyle.CreamSoft, TextAnchor.MiddleCenter);
            _pageLabel.rectTransform.Stretch(100f, 0f, 100f, 0f);
            _dots = UiStyle.PageDots(pager, "Dots", 8);
            _dots.Root.anchoredPosition = Vector2.zero;

            var close = UiStyle.PrimaryButton(panel, "CloseAlbum", "Zurück", new Vector2(680f, 124f), Close);
            ((RectTransform)close.transform).Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(680f, 124f));
        }

        static Button ArrowButton(RectTransform parent, string name, float anchorX, float rotation, Action onClick)
        {
            var b = UiStyle.IconButton(parent, name, 88f, onClick);
            ((RectTransform)b.transform).Place(new Vector2(anchorX, 0.5f), new Vector2(anchorX, 0.5f), Vector2.zero, new Vector2(88f, 88f));
            var glyph = UiStyle.Shape(b.transform, "Arrow", UiSprites.Arrow, UiStyle.Cream);
            glyph.rectTransform.Center(Vector2.zero, new Vector2(48f, 48f));
            glyph.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            return b;
        }

        void BuildView(RectTransform root)
        {
            var scrim = UiStyle.Scrim(root, "AlbumPhotoScreen", UiStyle.WithAlpha(UiStyle.Dim, 0.9f));
            _view = scrim.gameObject;
            UiStyle.FadeIn(_view, null);

            _date = UiStyle.Label(scrim, "", UiStyle.Subheading, UiStyle.Sand, TextAnchor.MiddleCenter, true);
            _date.rectTransform.TopCenter(new Vector2(0f, -56f), new Vector2(1000f, 60f));
            _date.horizontalOverflow = HorizontalWrapMode.Overflow;
            _position = UiStyle.Label(scrim, "", UiStyle.Caption, UiStyle.Muted, TextAnchor.MiddleCenter);
            _position.rectTransform.TopCenter(new Vector2(0f, -122f), new Vector2(1000f, 40f));

            _photo = UiStyle.Picture(scrim, "Photo", new Vector2(960f, 540f), out _photoFrame);
            _photoFrame.Center(Vector2.zero, new Vector2(960f, 540f));

            _confirmText = UiStyle.Label(scrim, "", UiStyle.Body, UiStyle.Cream, TextAnchor.MiddleCenter);
            _confirmText.rectTransform.BottomCenter(new Vector2(0f, 196f), new Vector2(1000f, 50f));

            var buttons = UiStyle.Rect(scrim, "ViewButtons").BottomCenter(new Vector2(0f, 60f), new Vector2(864f, 120f));
            _viewButtons = buttons.gameObject;
            Half(UiStyle.SecondaryButton(buttons, "DeletePhoto", "Löschen", new Vector2(420f, 120f), () => SetConfirm(true), true, true), 0f);
            var back = UiStyle.PrimaryButton(buttons, "BackToAlbum", "Zurück", new Vector2(420f, 120f), () => ShowGrid(_page));
            Half(back, 1f);
            UiStyle.ButtonIcon(back, UiIcon.Back);

            var confirm = UiStyle.Rect(scrim, "ConfirmButtons").BottomCenter(new Vector2(0f, 60f), new Vector2(864f, 120f));
            _confirmButtons = confirm.gameObject;
            Half(UiStyle.SecondaryButton(confirm, "ConfirmDelete", "Ja, löschen", new Vector2(420f, 120f), DeleteShown, true, true), 0f);
            Half(UiStyle.PrimaryButton(confirm, "KeepPhoto", "Behalten", new Vector2(420f, 120f), () => SetConfirm(false)), 1f);
            _confirmButtons.SetActive(false);
        }

        static void Half(Button b, float anchorX)
        {
            ((RectTransform)b.transform).Place(new Vector2(anchorX, 0.5f), new Vector2(anchorX, 0.5f), Vector2.zero, new Vector2(420f, 120f));
            UiStyle.LabelOf(b).fontSize = 44;
        }

        // ---------------------------------------------------------------- sample pictures

        static Texture2D SampleTexture(int index, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            float hue = Mathf.Repeat(0.52f + index * 0.043f, 1f);
            Color sea = Color.HSVToRGB(hue, 0.55f, 0.62f), sky = Color.HSVToRGB(Mathf.Repeat(hue + 0.06f, 1f), 0.25f + 0.04f * (index % 4), 0.95f);
            Color land = Color.HSVToRGB(0.27f + 0.03f * (index % 3), 0.5f, 0.66f), sand = new Color(0.93f, 0.85f, 0.62f);
            Vector2 c = new Vector2(0.35f + 0.07f * (index % 5), 0.4f);
            float r = 0.16f + 0.02f * (index % 4);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    Color col = v > 0.58f ? Color.Lerp(sky, Color.white, (v - 0.58f) * 0.9f) : Color.Lerp(sea * 0.8f, sea, v / 0.58f);
                    float d = new Vector2((u - c.x) * w / h, (v - c.y) * 1.9f).magnitude;
                    if (d < r * 1.18f) col = sand;
                    if (d < r) col = Color.Lerp(land, land * 0.8f, d / r);
                    px[y * w + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }
    }
}
