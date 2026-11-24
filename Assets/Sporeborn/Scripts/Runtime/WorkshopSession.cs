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

            if (IsPlaying)
            {
                if (!string.IsNullOrEmpty(_error))
                {
                    GUI.Box(new Rect(16f, 16f, Mathf.Min(Screen.width - 32f, 550f), 36f), _error);
                }

                return;
            }

            float width = Mathf.Min(420f, Screen.width - 24f);
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.18f, width, 330f), GUI.skin.box);
            GUILayout.Label("Sporeborn");
            GUILayout.Space(8f);
            if (_settings)
            {
                DrawSettings();
            }
            else if (_confirmNew)
            {
                GUILayout.Label("Начать новую демонстрацию? Текущий прогресс будет сброшен.");
                if (GUILayout.Button("Начать заново"))
                {
                    ConfirmNew();
                }

                if (GUILayout.Button("Отмена"))
                {
                    _confirmNew = false;
                }
            }
            else
            {
                GUI.enabled = _started || File.Exists(_path);
                if (GUILayout.Button("Продолжить"))
                {
                    Continue();
                }

                GUI.enabled = true;
                if (GUILayout.Button("Новая демонстрация"))
                {
                    _confirmNew = true;
                }

                if (GUILayout.Button("Настройки"))
                {
                    _settings = true;
                }

                if (GUILayout.Button("Выйти"))
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
                GUILayout.Label(_error);
            }

            GUILayout.EndArea();
        }

        private void DrawSettings()
        {
            GUILayout.Label($"Чувствительность мыши: {_player.Sensitivity:0.00}");
            float sensitivity = GUILayout.HorizontalSlider(_player.Sensitivity, 0.25f, 6f);
            GUILayout.Label($"Общая громкость: {Mathf.RoundToInt(AudioListener.volume * 100f)}%");
            float volume = GUILayout.HorizontalSlider(AudioListener.volume, 0f, 1f);
            if (!Mathf.Approximately(sensitivity, _player.Sensitivity) || !Mathf.Approximately(volume, AudioListener.volume))
            {
                _player.Sensitivity = sensitivity;
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
                PlayerPrefs.SetFloat(VolumeKey, volume);
                PlayerPrefs.Save();
            }

            if (GUILayout.Button("Назад"))
            {
                _settings = false;
            }
        }
    }
}
