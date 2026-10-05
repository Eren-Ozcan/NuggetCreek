using System.Collections.Generic;

namespace NuggetCreek.Core
{
    /// <summary>Old Pete's face in a tutorial step: the four drawings of his expression sheet.</summary>
    public enum PetePose
    {
        Talk,
        Point,
        Laugh,
        Wow,
    }

    /// <summary>What a tutorial step points at; the game maps each to a control or a spot on the water.</summary>
    public enum Cue
    {
        None,
        /// <summary>Gold Dust on the water; the hand swipes across it.</summary>
        Gold,
        /// <summary>The chest or boulder floating by; it waits on the water while Pete points.</summary>
        Floater,
        GoalButton,
        UpgradesButton,
        /// <summary>The first sluice upgrade's buy button.</summary>
        FirstUpgrade,
        AmosHire,
        CloseUpgrades,
        MapButton,
        /// <summary>The next creek's unlock button on the map.</summary>
        NextCreek,
        TrayChest,
        CrewChip,
        /// <summary>Either candidate's hire button.</summary>
        CandidateHire,
        CollectionButton,
        /// <summary>The card of the boss nugget just won.</summary>
        BossNugget,
        CloseCollection,
        DailyButton,
        GuildButton,
        ClaimBar,
        /// <summary>"Stake a new claim" in the Guild panel.</summary>
        StakeClaim,
    }

    /// <summary>What ends a tutorial step.</summary>
    public enum Until
    {
        /// <summary>A tap anywhere: Pete has said his piece.</summary>
        Tap,
        /// <summary>The cued control is pressed; nothing else on screen takes a tap meanwhile.</summary>
        Press,
        /// <summary><see cref="LessonStep.Count"/> more catches by hand on the open water.</summary>
        Catch,
        /// <summary>The floater on the water is caught.</summary>
        Floater,
    }

    /// <summary>One thing Pete says, and what the player must do before he goes on.</summary>
    public sealed class LessonStep
    {
        public readonly string Line;
        public readonly PetePose Pose;
        public readonly Cue Cue;
        public readonly Until Until;
        public readonly int Count;

        public LessonStep(string line, PetePose pose, Until until, Cue cue = Cue.None, int count = 0)
        {
            Line = line;
            Pose = pose;
            Until = until;
            Cue = cue;
            Count = count;
        }
    }

    /// <summary>
    /// Old Pete's lessons (design doc 9.1, 14.4): each <see cref="PeteLine"/> is a short script
    /// played once when its moment comes. The player cannot skip a step; a pointing step lets
    /// only the cued control take a tap. A step whose cue is not on screen (a goal that is not
    /// done yet, a panel already open) is passed over by the game.
    /// Lines stay short and dry, address the player as "kid", never sell, and name no tuning
    /// number, since the numbers come from the config.
    /// </summary>
    public static class Lessons
    {
        /// <summary>Longest line, so two lines fit on the speech panel.</summary>
        public const int MaxLineLength = 90;

        static readonly Dictionary<PeteLine, LessonStep[]> Scripts = new Dictionary<PeteLine, LessonStep[]>
        {
            [PeteLine.WelcomeBack] = new[]
            {
                new LessonStep("Welcome back, kid. Amos found this while you were gone.", PetePose.Point, Until.Press, Cue.TrayChest),
            },
            [PeteLine.Welcome] = new[]
            {
                new LessonStep("Name's Pete. Been panning this creek since before you were born.", PetePose.Talk, Until.Tap),
                new LessonStep("Something's glinting in the water. Run your finger across it.", PetePose.Point, Until.Catch, Cue.Gold, 5),
            },
            [PeteLine.FirstDust] = new[]
            {
                new LessonStep("That's gold dust. Keep at it and the creek pays.", PetePose.Laugh, Until.Tap),
                new LessonStep("The board up top says what the creek wants from you next.", PetePose.Point, Until.Tap, Cue.GoalButton),
            },
            [PeteLine.UpgradesOpen] = new[]
            {
                new LessonStep("Job's done. Collect your pay.", PetePose.Point, Until.Press, Cue.GoalButton),
                new LessonStep("Got a few dollars. A better shovel wouldn't hurt.", PetePose.Point, Until.Press, Cue.UpgradesButton),
                new LessonStep("That one. A sturdy shovel makes every speck pay more.", PetePose.Point, Until.Press, Cue.FirstUpgrade),
                new LessonStep("That'll do for now. Back to the water.", PetePose.Talk, Until.Press, Cue.CloseUpgrades),
            },
            [PeteLine.AmosForHire] = new[]
            {
                new LessonStep("Amos is looking for work. He pans even when you don't.", PetePose.Talk, Until.Tap),
                new LessonStep("He's waiting on the crew list.", PetePose.Point, Until.Press, Cue.UpgradesButton),
                new LessonStep("Hire him. Best money you'll spend today.", PetePose.Point, Until.Press, Cue.AmosHire),
                new LessonStep("Back to the water.", PetePose.Talk, Until.Press, Cue.CloseUpgrades),
                new LessonStep("Now the creek pays even while you're gone.", PetePose.Laugh, Until.Tap),
            },
            [PeteLine.FirstChest] = new[]
            {
                new LessonStep("A chest is floating by. Grab it!", PetePose.Wow, Until.Floater, Cue.Floater),
                new LessonStep("Open it. Could be anything in there.", PetePose.Point, Until.Press, Cue.TrayChest),
            },
            [PeteLine.MapOpen] = new[]
            {
                new LessonStep("Richer water upstream, and you can pay the claim now.", PetePose.Point, Until.Press, Cue.MapButton),
                new LessonStep("Stake it. The dredge will follow.", PetePose.Point, Until.Press, Cue.NextCreek),
            },
            [PeteLine.FreeHire] = new[]
            {
                new LessonStep("Two drifters want work.", PetePose.Point, Until.Press, Cue.CrewChip),
                new LessonStep("Pick one. First one's on me.", PetePose.Point, Until.Press, Cue.CandidateHire),
                new LessonStep("Good pick. Crew stay with you, even when you stake a new claim.", PetePose.Laugh, Until.Tap),
            },
            [PeteLine.DailyOpen] = new[]
            {
                new LessonStep("Come by every day. The creek rewards regulars.", PetePose.Point, Until.Press, Cue.DailyButton),
            },
            [PeteLine.GuildOpen] = new[]
            {
                new LessonStep("This bar fills as you earn. When it's full, a new claim pays off.", PetePose.Point, Until.Tap, Cue.ClaimBar),
                new LessonStep("The Prospectors' Guild has noticed you. Take a look.", PetePose.Point, Until.Press, Cue.GuildButton),
            },
            [PeteLine.FirstBoulder] = new[]
            {
                new LessonStep("That boulder's got gold in it. Grab it and crack it open!", PetePose.Wow, Until.Floater, Cue.Floater),
            },
            [PeteLine.FirstBoss] = new[]
            {
                new LessonStep("Now that's a nugget worth framing!", PetePose.Laugh, Until.Tap),
                new LessonStep("It goes in your book with the rest.", PetePose.Point, Until.Press, Cue.CollectionButton),
                new LessonStep("Every star in this book makes every claim pay more.", PetePose.Point, Until.Tap, Cue.BossNugget),
                new LessonStep("Each creek has a big one. Upstream they're tougher. You'll need a fresh claim or two.", PetePose.Talk, Until.Tap),
                new LessonStep("Back to work, kid.", PetePose.Talk, Until.Press, Cue.CloseCollection),
            },
            [PeteLine.ClaimReady] = new[]
            {
                new LessonStep("Creek's slowing down, kid. Upstream costs more than it pays.", PetePose.Wow, Until.Tap),
                new LessonStep("Your claim bar is full. Time to start fresh.", PetePose.Point, Until.Press, Cue.ClaimBar),
                new LessonStep("Back to Nugget Creek, richer for it. Crew, gear and Gems come along.", PetePose.Point, Until.Press, Cue.StakeClaim),
                new LessonStep("Sure? Tap it again.", PetePose.Talk, Until.Press, Cue.StakeClaim),
                new LessonStep("Every dollar pays more now. Watch it add up.", PetePose.Laugh, Until.Tap),
            },
        };

        /// <summary>The steps of a line's lesson, in order.</summary>
        public static IReadOnlyList<LessonStep> Of(PeteLine line) => Scripts[line];
    }
}
