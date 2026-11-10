using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent dual-source ambient sound controller across Stages 0-5.
/// Plays outdoor tracks in Stages 0-2, cave soundscapes in Stages 3-5 (balanced under BossBGM in Stage 5),
/// and fades out cleanly in Main Menu.
/// </summary>
public class StageAmbientAudio : MonoBehaviour
{
    #region Singleton & Constants

    public static StageAmbientAudio Instance { get; private set; }

    private const float CrossfadeDuration = 1.15f;
    private const float ExcludeFadeOutDuration = 0.85f;
    private const float Stage0VolumeMultiplier = 0.85f;
    private const float CaveStage34VolumeMultiplier = 0.68f;
    private const float Stage5CaveVolumeMultiplier = 0.40f;

#if UNITY_EDITOR
    private static readonly string[] EditorStageClipPaths =
    {
        "Assets/Audio/Ambient1.mp3",     // Stage0 (Tutorial outdoors)
        "Assets/Audio/Ambient1.mp3",     // Stage1 (Forest outskirts)
        "Assets/Audio/Ambient2.mp3",     // Stage2 (Deeper woods)
        "Assets/Audio/CaveAmbient3.wav", // Stage3 (Upper dripping cavern)
        "Assets/Audio/CaveAmbient4.wav", // Stage4 (Deep stalactite chasm)
        "Assets/Audio/CaveAmbient5.wav"  // Stage5 (Abyssal boss cave sanctuary)
    };
#endif

    #endregion

    #region Private State

    private readonly AudioClip[] stageClips = new AudioClip[6];
    private AudioSource sourceA;
    private AudioSource sourceB;
    private bool isSourceAActive = true;
    private float targetBaseVolume = 0.45f;
    private Coroutine transitionCoroutine;

    #endregion

    #region Public Entry Points

    public static void EnsureAndSync(AudioClip[] clipsByStageIndex, float volume, AudioMixerGroup mixerGroup)
    {
        if (Instance == null)
        {
            GameObject obj = new GameObject("StageAmbientAudio");
            DontDestroyOnLoad(obj);
            Instance = obj.AddComponent<StageAmbientAudio>();
        }

        Instance.CacheStageClips(clipsByStageIndex);
        Instance.targetBaseVolume = Mathf.Clamp01(volume);
        Instance.ApplyMixerGroup(mixerGroup);
        Instance.SyncToScene(SceneManager.GetActiveScene());
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        sourceA = CreateAmbientSource("AmbientSourceA");
        sourceB = CreateAmbientSource("AmbientSourceB");
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != this) return;
        SyncToScene(scene);
    }

    #endregion

    #region Audio Routing & Crossfades

    private AudioSource CreateAmbientSource(string childName)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        AudioSource src = child.AddComponent<AudioSource>();
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.priority = 24;
        src.volume = 0f;
        return src;
    }

    private void ApplyMixerGroup(AudioMixerGroup mixerGroup)
    {
        if (mixerGroup == null) return;
        if (sourceA != null) sourceA.outputAudioMixerGroup = mixerGroup;
        if (sourceB != null) sourceB.outputAudioMixerGroup = mixerGroup;
    }

    private void CacheStageClips(AudioClip[] clipsByStageIndex)
    {
        if (clipsByStageIndex != null)
        {
            int copyCount = Mathf.Min(stageClips.Length, clipsByStageIndex.Length);
            for (int i = 0; i < copyCount; i++)
            {
                if (clipsByStageIndex[i] != null)
                {
                    stageClips[i] = clipsByStageIndex[i];
                }
            }
        }

#if UNITY_EDITOR
        for (int i = 0; i < stageClips.Length && i < EditorStageClipPaths.Length; i++)
        {
            if (stageClips[i] == null)
            {
                stageClips[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(EditorStageClipPaths[i]);
            }
        }
#endif
    }

    private void SyncToScene(Scene scene)
    {
        if (!TryResolveStageClip(scene, out AudioClip desiredClip, out float desiredVolume))
        {
            FadeOutAll(ExcludeFadeOutDuration);
            return;
        }

        if (desiredClip == null)
        {
            FadeOutAll(ExcludeFadeOutDuration);
            return;
        }

        AudioSource active = isSourceAActive ? sourceA : sourceB;
        AudioSource inactive = isSourceAActive ? sourceB : sourceA;

        // Same clip already active (e.g. Stage0 -> Stage1 or restarting a stage after death): keep seamless playback
        if (active != null && active.isPlaying && active.clip == desiredClip)
        {
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
            transitionCoroutine = StartCoroutine(AdjustSingleSourceRoutine(active, inactive, desiredVolume, CrossfadeDuration * 0.7f));
            return;
        }

        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(CrossfadeRoutine(active, inactive, desiredClip, desiredVolume, CrossfadeDuration));
    }

    private bool TryResolveStageClip(Scene scene, out AudioClip clip, out float resolvedVolume)
    {
        clip = null;
        resolvedVolume = targetBaseVolume;

        if (FindFirstObjectByType<MainMenu>() != null)
        {
            return false;
        }

        string name = scene.name ?? string.Empty;
        if (string.Equals(name, "Stage", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int stageIndex = ResolveStageIndex(name);
        if (stageIndex < 0 || stageIndex >= stageClips.Length)
        {
            return false;
        }

        clip = stageClips[stageIndex];
        if (stageIndex == 0)
        {
            resolvedVolume *= Stage0VolumeMultiplier;
        }
        else if (stageIndex == 3 || stageIndex == 4)
        {
            resolvedVolume *= CaveStage34VolumeMultiplier;
        }
        else if (stageIndex == 5)
        {
            resolvedVolume *= Stage5CaveVolumeMultiplier;
        }
        return true;
    }

    private static int ResolveStageIndex(string sceneName)
    {
        if (sceneName.IndexOf("Stage0", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Stage 0", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
        if (sceneName.IndexOf("Stage1", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Stage 1", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
        if (sceneName.IndexOf("Stage2", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Stage 2", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        if (sceneName.IndexOf("Stage3", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Stage 3", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
        if (sceneName.IndexOf("Stage4", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Stage 4", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
        if (sceneName.IndexOf("Stage5", StringComparison.OrdinalIgnoreCase) >= 0
            || sceneName.IndexOf("Stage 5", StringComparison.OrdinalIgnoreCase) >= 0) return 5;
        return -1;
    }

    private void FadeOutAll(float duration)
    {
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(FadeOutRoutine(duration));
    }

    private IEnumerator AdjustSingleSourceRoutine(AudioSource active, AudioSource other, float targetVol, float duration)
    {
        float startActiveVol = active != null ? active.volume : 0f;
        float startOtherVol = other != null ? other.volume : 0f;
        float timer = 0f;
        float safeDur = Mathf.Max(0.05f, duration);

        while (timer < safeDur)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / safeDur);
            if (active != null) active.volume = Mathf.Lerp(startActiveVol, targetVol, t);
            if (other != null && other.isPlaying) other.volume = Mathf.Lerp(startOtherVol, 0f, t);
            yield return null;
        }

        if (active != null) active.volume = targetVol;
        if (other != null)
        {
            other.volume = 0f;
            if (other.isPlaying) other.Stop();
        }
        transitionCoroutine = null;
    }

    private IEnumerator CrossfadeRoutine(AudioSource outgoing, AudioSource incoming, AudioClip nextClip, float targetVol, float duration)
    {
        if (incoming != null)
        {
            incoming.clip = nextClip;
            incoming.volume = 0f;
            incoming.Play();
        }

        float startOutVol = outgoing != null ? outgoing.volume : 0f;
        float timer = 0f;
        float safeDur = Mathf.Max(0.05f, duration);

        while (timer < safeDur)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / safeDur);

            if (outgoing != null && outgoing.isPlaying)
            {
                outgoing.volume = Mathf.Lerp(startOutVol, 0f, t);
            }
            if (incoming != null)
            {
                incoming.volume = Mathf.Lerp(0f, targetVol, t);
            }
            yield return null;
        }

        if (outgoing != null)
        {
            outgoing.volume = 0f;
            if (outgoing.isPlaying) outgoing.Stop();
        }
        if (incoming != null)
        {
            incoming.volume = targetVol;
        }

        isSourceAActive = !isSourceAActive;
        transitionCoroutine = null;
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        float startA = sourceA != null ? sourceA.volume : 0f;
        float startB = sourceB != null ? sourceB.volume : 0f;
        float timer = 0f;
        float safeDur = Mathf.Max(0.05f, duration);

        while (timer < safeDur)
        {
            timer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(timer / safeDur);
            if (sourceA != null && sourceA.isPlaying) sourceA.volume = Mathf.Lerp(startA, 0f, t);
            if (sourceB != null && sourceB.isPlaying) sourceB.volume = Mathf.Lerp(startB, 0f, t);
            yield return null;
        }

        if (sourceA != null)
        {
            sourceA.volume = 0f;
            if (sourceA.isPlaying) sourceA.Stop();
        }
        if (sourceB != null)
        {
            sourceB.volume = 0f;
            if (sourceB.isPlaying) sourceB.Stop();
        }
        transitionCoroutine = null;
    }

    #endregion
}

