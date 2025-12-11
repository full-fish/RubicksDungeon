using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("오디오 소스")]
    public AudioSource sfxSource;
    public AudioSource bgmSource;

    [Header("BGM")]
    public AudioClip bgmClip;

    [Header("기본 SFX")]
    public AudioClip defaultWalk;
    public AudioClip defaultPush;
    public AudioClip defaultDestroy;
    
    [Header("게임 상태 SFX")]
    public AudioClip clipFail;
    public AudioClip clipClear;
    public AudioClip clipAllClear;
    public AudioClip clipShift;

    public void Init()
    {
        if (bgmSource != null && bgmClip != null)
        {
            bgmSource.clip = bgmClip;
            bgmSource.loop = true;
            bgmSource.Play();
        }
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
        {
            sfxSource.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            sfxSource.PlayOneShot(clip);
        }
    }

    // 타일 데이터 기반 재생
    public void PlayTileSound(TileData tile, SoundType type)
    {
        AudioClip clipToPlay = null;
        if (tile != null)
        {
            switch (type)
            {
                case SoundType.Walk: clipToPlay = tile.clipStep; break;
                case SoundType.Push: clipToPlay = tile.clipPush; break;
                case SoundType.Destroy: clipToPlay = tile.clipDestroy; break;
            }
        }

        if (clipToPlay == null)
        {
            switch (type)
            {
                case SoundType.Walk: clipToPlay = defaultWalk; break;
                case SoundType.Push: clipToPlay = defaultPush; break;
                case SoundType.Destroy: clipToPlay = defaultDestroy; break;
            }
        }
        PlaySFX(clipToPlay);
    }
}