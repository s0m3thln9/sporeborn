using System;
using System.IO;
using Sporeborn.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sporeborn.Runtime
{
    public sealed class WorkshopSession : MonoBehaviour
    {
        private const float AutosaveSeconds = 300f;
        private const string SensitivityKey = "Sporeborn.MouseSensitivity";
        private const string VolumeKey = "Sporeborn.Volume";

        private static bool _newAfterReload;
        private WorkshopBatch _batch;
        private FirstPersonPlayer _player;
        private MushroomInteraction _interaction;
        private string _path;
        private string _error;
        private float _sinceSave;
        private bool _started;
        private bool _confirmNew;
        private bool _settings;
        private bool _resetting;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _cardStyle;

        public bool IsPlaying { get; private set; }

        internal void Initialize(WorkshopBatch batch)
        {
            _batch = batch;
            _player = FindAnyObjectByType<FirstPersonPlayer>();
            _interaction = _player != null ? _player.GetComponent<MushroomInteraction>() : null;
            if (_interaction == null)
            {
                Debug.LogError("WorkshopSession requires the workshop player with MushroomInteraction.", this);
                enabled = false;
                return;
            }

            _path = Path.Combine(Application.persistentDataPath, "workshop-save.json");
            _player.Sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, _player.Sensitivity), 0.25f, 6f);
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            if (_newAfterReload)
            {
                _newAfterReload = false;
                BeginNew();
            }
            else
            {
                Pause();
            }
        }

        internal void Pause()
        {
            SetPlaying(false);
            _interaction.ClearAim();
        }

        private void Update()
        {
            if (!_started || _resetting)
            {
                return;
            }

            _sinceSave += Time.unscaledDeltaTime;
            if (_sinceSave >= AutosaveSeconds)
            {
                Save();
            }
        }

        private void OnApplicationQuit()
        {
            if (_started && !_resetting)
            {
                Save();
            }
        }

        private void OnDestroy()
        {
            if (!_resetting)
            {
                Time.timeScale = 1f;
            }
        }

        private void Save()
        {
            _sinceSave = 0f;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                WorkshopSaveFile.Write(_path, _batch.Capture(_player));
                _error = null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _error = "Не удалось сохранить демонстрацию. Проверьте доступ к папке сохранений.";
                Debug.LogWarning(exception.Message, this);
            }
        }

        private void Continue()
        {
            if (!_started)
            {
                try
                {
                    _batch.Resume(WorkshopSaveFile.Read(_path), _player, _interaction);
                    _started = true;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                    exception is ArgumentException || exception is InvalidDataException)
                {
                    _error = $"Не удалось загрузить сохранение. {exception.Message}";
                    return;
                }
            }

            _error = null;
            SetPlaying(true);
        }

        private void BeginNew()
        {
            _started = true;
            _confirmNew = false;
            Save();
            SetPlaying(true);
        }

        private void ConfirmNew()
        {
            if (!_started)
            {
                BeginNew();
                return;
            }

            _resetting = true;
            _newAfterReload = true;
            SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        private void SetPlaying(bool playing)
        {
            IsPlaying = playing;
            Time.timeScale = playing ? 1f : 0f;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
        }

        private void OnGUI()
        {
            if (_batch == null || _interaction == null)
            {
                return;
            }

            float scale = WorkshopGui.Scale;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                DrawMenu(Screen.width / scale, Screen.height / scale);
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        private void DrawMenu(float screenWidth, float screenHeight)
        {
            _headerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            _labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            _buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 38f };
            _cardStyle ??= new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 16, 16) };

            if (IsPlaying)
            {
                if (!string.IsNullOrEmpty(_error))
                {
                    GUI.Label(new Rect(16f, 16f, Mathf.Min(screenWidth - 32f, 550f), 58f), _error, _labelStyle);
                }

                return;
            }

            float width = Mathf.Min(440f, screenWidth - 24f);
            GUI.DrawTexture(new Rect(0f, 0f, screenWidth, screenHeight), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0f, new Color(0.03f, 0.07f, 0.09f, 0.54f), 0f, 0f);
            GUILayout.BeginArea(new Rect((screenWidth - width) * 0.5f, screenHeight * 0.22f, width, 420f));
            GUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Sporeborn", _headerStyle);
            GUILayout.Space(8f);
            if (_settings)
            {
                DrawSettings();
            }
            else if (_confirmNew)
            {
                GUILayout.Label("Начать новую демонстрацию? Текущий прогресс будет сброшен.", _labelStyle);
                if (GUILayout.Button("Начать заново", _buttonStyle))
                {
                    ConfirmNew();
                }

                if (GUILayout.Button("Отмена", _buttonStyle))
                {
                    _confirmNew = false;
                }
            }
            else
            {
                GUI.enabled = _started || File.Exists(_path);
                if (GUILayout.Button("Продолжить", _buttonStyle))
                {
                    Continue();
                }

                GUI.enabled = true;
                if (GUILayout.Button("Новая демонстрация", _buttonStyle))
                {
                    _confirmNew = true;
                }

                if (GUILayout.Button("Настройки", _buttonStyle))
                {
                    _settings = true;
                }

                if (GUILayout.Button("Выйти", _buttonStyle))
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }

            if (!string.IsNullOrEmpty(_error))
            {
                GUILayout.Space(8f);
                GUILayout.Label(_error, _labelStyle);
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void DrawSettings()
        {
            GUILayout.Label($"Чувствительность мыши: {_player.Sensitivity:0.00}", _labelStyle);
            float sensitivity = GUILayout.HorizontalSlider(_player.Sensitivity, 0.25f, 6f);
            GUILayout.Label($"Общая громкость: {Mathf.RoundToInt(AudioListener.volume * 100f)}%", _labelStyle);
            float volume = GUILayout.HorizontalSlider(AudioListener.volume, 0f, 1f);
            if (!Mathf.Approximately(sensitivity, _player.Sensitivity) || !Mathf.Approximately(volume, AudioListener.volume))
            {
                _player.Sensitivity = sensitivity;
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
                PlayerPrefs.SetFloat(VolumeKey, volume);
                PlayerPrefs.Save();
            }

            if (GUILayout.Button("Назад", _buttonStyle))
            {
                _settings = false;
            }
        }
    }
}
