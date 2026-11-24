using UnityEngine;

namespace Sporeborn.Runtime
{
    [RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public sealed class WorkshopSign : MonoBehaviour
    {
        private TextMesh _text;
        private MeshRenderer _renderer;
        private Camera _view;
        private float _size;
        private Vector2 _limits;

        public string Text
        {
            get => _text.text;
            set
            {
                if (_text.text == value)
                {
                    return;
                }

                _text.text = value;
                Fit();
            }
        }

        internal void Configure(string text, float size, Color color, Vector2 limits)
        {
            _view = Camera.main;
            _text = GetComponent<TextMesh>();
            _renderer = GetComponent<MeshRenderer>();
            _size = size;
            _limits = limits;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 32;
            _text.color = color;
            _text.text = text;
            Fit();
            FaceViewer();
        }

        private void Fit()
        {
            _text.characterSize = _size;
            Vector3 bounds = _renderer.localBounds.size;
            float width = bounds.x > 0f ? _limits.x / bounds.x : 1f;
            float height = bounds.y > 0f ? _limits.y / bounds.y : 1f;
            _text.characterSize = _size * Mathf.Min(1f, width, height);
        }

        private void LateUpdate()
        {
            FaceViewer();
        }

        private void FaceViewer()
        {
            _renderer.enabled = _view != null && Vector3.Dot(-transform.forward, _view.transform.position - transform.position) > 0f;
        }
    }
}
