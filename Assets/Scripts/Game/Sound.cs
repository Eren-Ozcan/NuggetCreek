using System.Collections.Generic;
using NuggetCreek.Core;
using UnityEngine;

namespace NuggetCreek.Game
{
    public enum Sfx
    {
        Upgrade,
        Region,
        VeinStart,
        VeinHit,
        VeinEnd,
        Chest,
        Goal,
        Crew,
        Tap,
    }

    /// <summary>
    /// Music and sound effects (design doc 15). Clips come from Resources/Audio (kept in the
    /// private pictures repo like the art, named as in the audio prompt sheet); a missing clip
    /// is silent, so the game runs without any audio. The music is one loop with layers that
    /// fade in and out on the game state (15.3); effects share <see cref="SoundRules.MaxVoices"/>
    /// voices, and back-to-back catches climb the scale (15.5).
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        const float FadePerSecond = 1.5f;
        const int PickVariants = 6;

        static Sound instance;

        /// <summary>Settings; the game sets them from the save and the Settings panel.</summary>
        public static bool MusicOn { get; set; } = true;
        public static bool SoundOn { get; set; } = true;

        /// <summary>Game state the music follows, set by the game every frame.</summary>
        public static int Region { get; set; }
        public static bool MotherLode { get; set; }

        /// <summary>True while a full-screen ad plays: everything fades out (15.5).</summary>
        public static bool AdPlaying { get; set; }

        sealed class Layer
        {
            public AudioSource Source;
            public float Level;
        }

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioClip> picks = new List<AudioClip>();
        AudioSource[] voices;
        Layer bed, dulcimer, harmonica, percussion;
        float master = 1;
        int pickRun;
        double lastPickAt = -100;
        double lastSwipeAt = -100;
        int bedRegion = -1;

        /// <summary>Builds the audio host once; later calls return the existing one.</summary>
        public static void Create()
        {
            if (instance != null)
                return;
            // The scene's camera carries no listener; the game's only ears live here.
            var go = new GameObject("Sound", typeof(AudioListener));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Sound>();
        }

        public static void Play(Sfx sfx)
        {
            if (instance != null)
                instance.PlayEffect(sfx);
        }

        /// <summary>A catch: a random pick sound, a degree higher than the last one in the run.</summary>
        public static void Pick(bool manual)
        {
            if (instance == null)
                return;
            double now = Time.unscaledTimeAsDouble;
            if (manual)
                instance.lastSwipeAt = now;
            instance.pickRun = now - instance.lastPickAt > SoundRules.PickRunGapSeconds ? 0 : instance.pickRun + 1;
            instance.lastPickAt = now;
            if (instance.picks.Count > 0)
            {
                AudioClip clip = instance.picks[Random.Range(0, instance.picks.Count)];
                // Catches are the one sound allowed to drop when all voices are busy.
                instance.Voice(clip, SoundRules.PickPitch(instance.pickRun), steal: false);
            }
        }

        void Awake()
        {
            voices = new AudioSource[SoundRules.MaxVoices];
            for (int i = 0; i < voices.Length; i++)
                voices[i] = NewSource(false);
            for (int i = 1; i <= PickVariants; i++)
            {
                AudioClip clip = Clip($"SFX/sfx_pick_{i:00}");
                if (clip != null)
                    picks.Add(clip);
            }
            bed = NewLayer("music_base_loop");
            dulcimer = NewLayer("music_layer_dulcimer");
            harmonica = NewLayer("music_layer_harmonica");
            percussion = NewLayer("music_layer_percussion");
            StartLayersTogether();
        }

        void Update()
        {
            float step = FadePerSecond * Time.unscaledDeltaTime;
            master = Mathf.MoveTowards(master, AdPlaying ? 0 : 1, step);
            float music = MusicOn ? master * SoundRules.Gain(SoundRules.MusicDb + (MotherLode ? SoundRules.MotherLodeDuckDb : 0)) : 0;
            bool swiping = Time.unscaledTimeAsDouble - lastSwipeAt < SoundRules.HarmonicaHoldSeconds;

            FollowRegionColour();
            Fade(bed, 1, music, step);
            Fade(dulcimer, Region >= SoundRules.DulcimerFromRegion ? 1 : 0, music, step);
            Fade(harmonica, swiping ? 1 : 0, music, step);
            Fade(percussion, MotherLode ? 1 : 0, music, step);

            float effects = SoundOn ? master : 0;
            foreach (AudioSource voice in voices)
                voice.volume = effects;
        }

        /// <summary>A Mother Lode hit, a degree higher with each step of the combo.</summary>
        public static void VeinHit(int combo)
        {
            if (instance != null)
                instance.PlayEffect(Sfx.VeinHit, SoundRules.PickPitch(combo - 1));
        }

        void PlayEffect(Sfx sfx, float pitch = 1)
        {
            AudioClip clip = Clip("SFX/" + FileName(sfx));
            if (clip != null)
                Voice(clip, pitch, steal: true);
        }

        void Voice(AudioClip clip, float pitch, bool steal)
        {
            if (!SoundOn)
                return;
            AudioSource free = null;
            AudioSource oldest = null;
            foreach (AudioSource voice in voices)
            {
                if (!voice.isPlaying)
                {
                    free = voice;
                    break;
                }
                if (oldest == null || voice.time > oldest.time)
                    oldest = voice;
            }
            AudioSource target = free ?? (steal ? oldest : null);
            if (target == null)
                return;
            target.clip = clip;
            target.pitch = pitch;
            target.Play();
        }

        /// <summary>A creek with its own colour of the loop swaps the bed in place; tempo and key never change.</summary>
        void FollowRegionColour()
        {
            if (Region == bedRegion)
                return;
            bedRegion = Region;
            AudioClip clip = null;
            if (Region >= 0 && Region < GameCatalog.RegionNames.Count)
                clip = Clip("Music/music_region_" + GameCatalog.RegionNames[Region].ToLowerInvariant().Replace(' ', '_'));
            clip = clip != null ? clip : Clip("Music/music_base_loop");
            if (clip == null || bed.Source.clip == clip)
                return;
            int position = bed.Source.clip != null ? bed.Source.timeSamples % clip.samples : 0;
            bed.Source.clip = clip;
            bed.Source.timeSamples = position;
            bed.Source.Play();
        }

        static void Fade(Layer layer, float target, float music, float step)
        {
            if (layer.Source.clip == null)
                return;
            layer.Level = Mathf.MoveTowards(layer.Level, target, step);
            layer.Source.volume = layer.Level * music;
        }

        /// <summary>All layers start on the same sample so they stay locked together while looping.</summary>
        void StartLayersTogether()
        {
            double at = AudioSettings.dspTime + 0.2;
            foreach (Layer layer in new[] { bed, dulcimer, harmonica, percussion })
                if (layer.Source.clip != null)
                    layer.Source.PlayScheduled(at);
        }

        Layer NewLayer(string file)
        {
            AudioSource source = NewSource(true);
            source.clip = Clip("Music/" + file);
            return new Layer { Source = source };
        }

        AudioSource NewSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = 0;
            return source;
        }

        AudioClip Clip(string path)
        {
            if (!clips.TryGetValue(path, out AudioClip clip))
            {
                clip = Resources.Load<AudioClip>("Audio/" + path);
                clips[path] = clip;
            }
            return clip;
        }

        /// <summary>File names from the audio prompt sheet (section 6).</summary>
        static string FileName(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Upgrade: return "sfx_upgrade";
                case Sfx.Region: return "sfx_region";
                case Sfx.VeinStart: return "sfx_vein_start";
                case Sfx.VeinHit: return "sfx_vein_hit";
                case Sfx.VeinEnd: return "sfx_vein_end";
                case Sfx.Chest: return "sfx_chest";
                case Sfx.Goal: return "sfx_goal";
                case Sfx.Crew: return "sfx_crew";
                default: return "sfx_ui_tap";
            }
        }
    }
}
