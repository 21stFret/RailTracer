using System;
using System.Collections.Generic;
using UnityEngine;

namespace RailTracer
{
    // The tutorial: the opening walkthrough and the first-time cards (wording from the Rail Tracer tutorial script),
    // queued and shown one at a time, with progress kept in PlayerPrefs (this machine is the save).
    // Game decides when events happen and what each card highlights; this only remembers and queues.
    public class Tutorial
    {
        const string Key = "RailTracer.Tutorial.";

        public class Card
        {
            public string id, title, text, tip, number;
            public Func<IList<Rect>> targets;  // screen rects to highlight (null = none, card in the centre)
            public Func<Vector3?> focus;       // where the camera pans first (null = stays)
            public bool last;                  // the walkthrough's final card
            public bool shown;
            public float panUntil = -1f;
        }

        // The opening walkthrough, in order: title, text, tip.
        public static readonly (string title, string text, string tip)[] Walkthrough =
        {
            ("Tickets please", "You're the conductor. Tonight this carriage is full of unruly passengers, and you're here to punch their tickets.", null),
            ("Health", "This is your health. Be careful, you don't have much.", null),
            ("Actions", "Each turn you get four moves and one action. Crouch behind seats to stay small, and watch the eye: when it's open, someone can see you.", null),
            ("Skills", "Press 1 to 8 to pick a skill. Grey is locked, white is ready, and red is bloodied, stronger than before. If there's no valid target, the box shows hollow.", "Hover a box to read it."),
            ("Legend", "Every kill feeds your legend. Each notch unlocks a skill or bloodies one. Your legend also shields your health: hits drain it first.", null),
            ("Passengers", "Everyone wears their nerve above their head. A ring means they haven't noticed you. The bars are their terror: fill all three and they break.", "Hover anyone to read them in full."),
            ("Tickets", "Kill a passenger or break them, and their ticket is yours. Punch every ticket to clear the carriage.", null),
        };

        // First-time cards by id: title, text. (New skill and Bloodied fill in the skill.)
        public static readonly Dictionary<string, (string title, string text)> FirstTime = new Dictionary<string, (string, string)>
        {
            ["searching"] = ("Searching", "They've noticed something and they're looking for you. Searchers follow blood, spot you right beside them even crouched, and they will shoot."),
            ["startled"] = ("Startled", "They freeze for a moment. Only someone who's never seen you can be startled, so make it count."),
            ["combat"] = ("In combat", "A red chip means they can see you. A crosshair means they'll shoot if you end your turn there. Get out of sight."),
            ["seen"] = ("Seen", "The eye on their chip means they've seen you, and they won't forget. They can't be startled again."),
            ["reaction"] = ("Reaction fire", "Red on your path means a gun is covering that tile. Step onto it, or leap across it, and they fire."),
            ["bodies"] = ("Bodies", "Grab a body for free and drag it at half speed. Blood leaves a trail they'll follow. Drag a body away in front of a witness, and watch them panic."),
            ["unlock"] = ("New skill", "{0} is ready."),
            ["bloodied"] = ("Bloodied", "{0} is bloodied: {1}"),
            ["broken"] = ("Broken", "They're too scared to fight and have given up. The white flag means they're out of play, and their ticket is yours."),
            ["ticket"] = ("Ticket punched", "Red for a kill, white for a break."),
            ["targeting"] = ("Targeting", "The banner shows which skill you're using. Pick a highlighted target, or press Esc to cancel."),
        };

        readonly Queue<Card> queue = new Queue<Card>();
        public Card Current { get; private set; }
        public bool Active => Current != null || queue.Count > 0;

        static bool Flag(string name) => PlayerPrefs.GetInt(Key + name, 0) == 1;
        static void SetFlag(string name, bool on) { PlayerPrefs.SetInt(Key + name, on ? 1 : 0); PlayerPrefs.Save(); }

        // Skip tutorial turns off every remaining card; the pause menu's Tutorial tips turns first-time cards back on.
        public bool Skipped => Flag("Skipped");
        public bool WantsWalkthrough => !Skipped && !Flag("Walkthrough");

        public void QueueWalkthrough(Func<int, Func<IList<Rect>>> targets, Func<int, Func<Vector3?>> focus)
        {
            for (int i = 0; i < Walkthrough.Length; i++)
                queue.Enqueue(new Card
                {
                    title = Walkthrough[i].title, text = Walkthrough[i].text, tip = Walkthrough[i].tip,
                    number = $"{i + 1} / {Walkthrough.Length}", targets = targets(i), focus = focus(i), last = i == Walkthrough.Length - 1,
                });
        }

        // Queues a first-time card the first time its event happens (ever, on this machine). False if it's had its turn.
        public bool Once(string id, Func<IList<Rect>> targets, Func<Vector3?> focus, params object[] fill)
        {
            if (Skipped || Flag(id)) return false;
            SetFlag(id, true); // marked on the event, so a lasting condition queues it once
            var (title, text) = FirstTime[id];
            queue.Enqueue(new Card { id = id, title = title, text = string.Format(text, fill), targets = targets, focus = focus });
            return true;
        }

        // The card to show now (moves on to the next queued one when none is up).
        public Card Step()
        {
            if (Current == null && queue.Count > 0) Current = queue.Dequeue();
            return Current;
        }

        public void Next()
        {
            if (Current != null && Current.last) SetFlag("Walkthrough", true);
            Current = null;
        }

        public void Skip()
        {
            SetFlag("Skipped", true);
            SetFlag("Walkthrough", true);
            queue.Clear();
            Current = null;
        }

        // Tutorial tips on: first-time cards come back (all of them, shown or not); off: they stop.
        public void SetTips(bool on)
        {
            if (on) foreach (var id in FirstTime.Keys) SetFlag(id, false);
            SetFlag("Skipped", !on);
            if (!on) { queue.Clear(); Current = null; }
        }

        // Debug: forget all tutorial progress, as on a new save.
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key + "Skipped");
            PlayerPrefs.DeleteKey(Key + "Walkthrough");
            foreach (var id in FirstTime.Keys) PlayerPrefs.DeleteKey(Key + id);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            queue.Clear();
            Current = null;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Rail Tracer/Reset Tutorial")]
        static void Menu() { ResetAll(); Debug.Log("Rail Tracer: tutorial progress reset."); }
#endif
    }
}
