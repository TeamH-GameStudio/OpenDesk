using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;

namespace OpenDesk.Core.Persistence
{
    /// <summary>
    /// 일정 간격으로 IGameDataService.SaveAllData를 호출하는 자동 저장 서비스.<br/>
    /// VContainer의 ITickable / IDisposable로 동작 — Tick은 매 프레임 호출된다.<br/>
    /// ProjectH의 NewSystem.AutoSaveService에서 이식, BackEnd.IsLogin 체크 제거.
    /// </summary>
    public class AutoSaveService : ITickable, IDisposable
    {
        private const float DEFAULT_AUTO_SAVE_INTERVAL = 300f; // 5분
        private const float MIN_AUTO_SAVE_INTERVAL = 10f;

        private readonly IGameDataService _gameDataService;

        private float _autoSaveInterval = DEFAULT_AUTO_SAVE_INTERVAL;
        private float _timeSinceLastSave;
        private bool _autoSaveEnabled = true;
        private bool _savingInProgress;
        private bool _isShuttingDown;
        private CancellationTokenSource _cts;

        public float AutoSaveInterval
        {
            get => _autoSaveInterval;
            set => _autoSaveInterval = Mathf.Max(MIN_AUTO_SAVE_INTERVAL, value);
        }

        public bool AutoSaveEnabled
        {
            get => _autoSaveEnabled;
            set => _autoSaveEnabled = value;
        }

        public float TimeSinceLastSave => _timeSinceLastSave;
        public bool IsSavingInProgress => _savingInProgress;

        public AutoSaveService(IGameDataService gameDataService)
        {
            _gameDataService = gameDataService ?? throw new ArgumentNullException(nameof(gameDataService));
            _cts = new CancellationTokenSource();
            _timeSinceLastSave = 0f;
        }

        public void Tick()
        {
            if (!_autoSaveEnabled || _isShuttingDown)
                return;

            _timeSinceLastSave += Time.deltaTime;

            if (_timeSinceLastSave >= _autoSaveInterval && !_savingInProgress)
            {
                SaveAutomaticallyAsync().Forget();
            }
        }

        private async UniTaskVoid SaveAutomaticallyAsync()
        {
            if (_savingInProgress || _isShuttingDown)
                return;

            _savingInProgress = true;

            try
            {
                if (_isShuttingDown || _cts.Token.IsCancellationRequested)
                    return;

                await _gameDataService.SaveAllData();
            }
            catch (OperationCanceledException)
            {
                // 정상 취소 — 무시.
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoSaveService] 자동 저장 오류: {e.Message}");
            }
            finally
            {
                _timeSinceLastSave = 0f;
                _savingInProgress = false;
            }
        }

        public async UniTask<bool> SaveManuallyAsync()
        {
            if (_savingInProgress || _isShuttingDown)
                return false;

            _savingInProgress = true;
            var result = false;

            try
            {
                result = await _gameDataService.SaveAllData();

                if (result)
                {
                    _timeSinceLastSave = 0f;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AutoSaveService] 수동 저장 중 오류: {e.Message}");
            }
            finally
            {
                _savingInProgress = false;
            }

            return result;
        }

        public void ResetTimer() => _timeSinceLastSave = 0f;

        private bool _isDisposed;

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _isShuttingDown = true;
            _autoSaveEnabled = false;
            _timeSinceLastSave = 0f;
            _savingInProgress = false;

            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 이미 Dispose된 경우 무시.
            }

            _cts?.Dispose();
            _cts = null;
        }
    }
}
