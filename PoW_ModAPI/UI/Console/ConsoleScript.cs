using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModAPI
{
    //Shamelessly ripped from https://github.com/mminer/consolation/blob/master/Console.cs :)
    /// <summary>
    /// A console to display Unity's debug logs in-game.
    ///
    /// Version: 1.1.2
    /// </summary>
    class ConsoleScript : MonoBehaviour
    {
        #region Inspector Settings

        /// <summary>
        /// The hotkey to show and hide the console window.
        /// </summary>
        public KeyCode toggleKey = KeyCode.F9;

        /// <summary>
        /// Whether to open as soon as the game starts.
        /// </summary>
        public bool openOnStart = false;

        /// <summary>
        /// Whether to open the window by shaking the device (mobile-only).
        /// </summary>
        public bool shakeToOpen = false;

        /// <summary>
        /// Also require touches while shaking to avoid accidental shakes.
        /// </summary>
        public bool shakeRequiresTouch = false;

        /// <summary>
        /// The (squared) acceleration above which the window should open.
        /// </summary>
        public float shakeAcceleration = 3f;

        /// <summary>
        /// The number of seconds that have to pass between visibility toggles.
        /// This threshold prevents closing again while shaking to open.
        /// </summary>
        public float toggleThresholdSeconds = .5f;
        float lastToggleTime;

        /// <summary>
        /// Whether to only keep a certain number of logs, useful if memory usage is a concern.
        /// </summary>
        public bool restrictLogCount = true;

        /// <summary>
        /// Number of logs to keep before removing old ones.
        /// </summary>
        public int maxLogCount = 500;

        /// <summary>
        /// Font size to display log entries with.
        /// </summary>
        public int logFontSize = 12;

        /// <summary>
        /// Amount to scale UI by.
        /// </summary>
        public float scaleFactor = 1f;

        #endregion

        static readonly GUIContent clearLabel = new GUIContent("Clear", "Clear the contents of the console.");
        static readonly GUIContent collapseLabel = new GUIContent("Collapse", "Hide repeated messages.");
        const int margin = 20;
        const string windowTitle = "Console";

        static readonly Dictionary<LogType, Color> logTypeColors = new Dictionary<LogType, Color>
        {
            { LogType.Assert, Color.white },
            { LogType.Error, Color.red },
            { LogType.Exception, Color.red },
            { LogType.Log, Color.white },
            { LogType.Warning, Color.yellow },
        };

        bool isCollapsed;
        bool isVisible;
        readonly List<Log> logs = new List<Log>();
        readonly ConcurrentQueue<Log> queuedLogs = new ConcurrentQueue<Log>();

        Vector2 scrollPosition;
        readonly Rect titleBarRect = new Rect(0, 0, 10000, 20);
        float windowX = margin;
        float windowY = margin;

        readonly Dictionary<LogType, bool> logTypeFilters = new Dictionary<LogType, bool>
        {
            { LogType.Assert, true },
            { LogType.Error, true },
            { LogType.Exception, true },
            { LogType.Log, true },
            { LogType.Warning, true },
        };

        #region MonoBehaviour Messages

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= HandleLogThreaded;
        }

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += HandleLogThreaded;
        }

        void OnGUI()
        {
            if (!isVisible)
            {
                return;
            }

            GUI.matrix = Matrix4x4.Scale(Vector3.one * scaleFactor);

            float width = (Screen.width / scaleFactor) - (margin * 2);
            float height = 250;//(Screen.height / scaleFactor) - (margin * 2);
            Rect windowRect = new Rect(windowX, windowY, width, height);

            GUI.backgroundColor = Color.black;
            Rect newWindowRect = GUILayout.Window(123456, windowRect, DrawWindow, windowTitle);
            windowX = newWindowRect.x;
            windowY = newWindowRect.y;
        }

        void Start()
        {
            if (openOnStart)
            {
                isVisible = true;
            }
        }

        void Update()
        {
            UpdateQueuedLogs();

            float curTime = Time.realtimeSinceStartup;

            if (Input.GetKeyDown(toggleKey))
            {
                isVisible = !isVisible;
            }

            if (shakeToOpen &&
                Input.acceleration.sqrMagnitude > shakeAcceleration &&
                curTime - lastToggleTime >= toggleThresholdSeconds &&
                (!shakeRequiresTouch || Input.touchCount > 2))
            {
                isVisible = !isVisible;
                lastToggleTime = curTime;
            }
        }

        #endregion

        void DrawLog(Log log, GUIStyle logStyle, GUIStyle badgeStyle)
        {
            GUI.contentColor = logTypeColors[log.type];

            if (isCollapsed)
            {
                // Draw collapsed log with badge indicating count.
                GUILayout.BeginHorizontal();
                GUILayout.Label(log.GetTruncatedMessage(), logStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(log.count.ToString(), GUI.skin.box);
                GUILayout.EndHorizontal();
            }
            else
            {
                // Draw expanded log.
                for (var i = 0; i < log.count; i += 1)
                {
                    GUILayout.Label(log.GetTruncatedMessage(), logStyle);
                }
            }

            GUI.contentColor = Color.white;
        }

        void DrawLogList()
        {
            GUIStyle badgeStyle = GUI.skin.box;
            badgeStyle.fontSize = logFontSize;

            GUIStyle logStyle = GUI.skin.label;
            logStyle.fontSize = logFontSize;

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            // Used to determine height of accumulated log labels.
            GUILayout.BeginVertical();

            var visibleLogs = logs.Where(IsLogVisible);

            foreach (Log log in visibleLogs)
            {
                DrawLog(log, logStyle, badgeStyle);
            }

            GUILayout.EndVertical();
            var innerScrollRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndScrollView();
            var outerScrollRect = GUILayoutUtility.GetLastRect();

            // If we're scrolled to bottom now, guarantee that it continues to be in next cycle.
            if (Event.current.type == EventType.Repaint && IsScrolledToBottom(innerScrollRect, outerScrollRect))
            {
                ScrollToBottom();
            }
        }

        void DrawToolbar()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            if (GUILayout.Button(clearLabel))
            {
                logs.Clear();
            }

            if (GUILayout.Button("Reload Scene"))
            {
                Scene scene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(scene.name);
            }
            GUILayout.EndVertical();

            foreach (LogType logType in Enum.GetValues(typeof(LogType)))
            {
                var currentState = logTypeFilters[logType];
                var label = logType.ToString();
                logTypeFilters[logType] = GUILayout.Toggle(currentState, label, GUILayout.ExpandWidth(false));
                GUILayout.Space(20);
            }

            isCollapsed = GUILayout.Toggle(isCollapsed, collapseLabel, GUILayout.ExpandWidth(false));

            GUILayout.EndHorizontal();
        }

        void DrawWindow(int windowID)
        {

            DrawLogList();
            DrawToolbar();

            // Allow the window to be dragged by its title bar.
            GUI.DragWindow(titleBarRect);
        }

        Log? GetLastLog()
        {
            if (logs.Count == 0)
            {
                return null;
            }

            return logs.Last();
        }

        void UpdateQueuedLogs()
        {
            Log log;
            while (queuedLogs.TryDequeue(out log))
            {
                ProcessLogItem(log);
            }
        }

        void HandleLogThreaded(string message, string stackTrace, LogType type)
        {
            var log = new Log
            {
                count = 1,
                message = message,
                stackTrace = stackTrace,
                type = type,
            };

            // Queue the log into a ConcurrentQueue to be processed later in the Unity main thread,
            // so that we don't get GUI-related errors for logs coming from other threads
            queuedLogs.Enqueue(log);
        }

        void ProcessLogItem(Log log)
        {
            var lastLog = GetLastLog();
            var isDuplicateOfLastLog = lastLog.HasValue && log.Equals(lastLog.Value);

            if (isDuplicateOfLastLog)
            {
                // Replace previous log with incremented count instead of adding a new one.
                log.count = lastLog.Value.count + 1;
                logs[logs.Count - 1] = log;
                //A message repeated over and over counts too, the expanded view draws it once per repeat
                TrimExcessLogs();
            }
            else
            {
                logs.Add(log);
                TrimExcessLogs();
            }
        }

        bool IsLogVisible(Log log)
        {
            return logTypeFilters[log.type];
        }

        bool IsScrolledToBottom(Rect innerScrollRect, Rect outerScrollRect)
        {
            var innerScrollHeight = innerScrollRect.height;

            // Take into account extra padding added to the scroll container.
            var outerScrollHeight = outerScrollRect.height - GUI.skin.box.padding.vertical;

            // If contents of scroll view haven't exceeded outer container, treat it as scrolled to bottom.
            if (outerScrollHeight > innerScrollHeight)
            {
                return true;
            }

            // Scrolled to bottom (with error margin for float math)
            return Mathf.Approximately(innerScrollHeight, scrollPosition.y + outerScrollHeight);
        }

        void ScrollToBottom()
        {
            scrollPosition = new Vector2(0, Int32.MaxValue);
        }

        void TrimExcessLogs()
        {
            if (!restrictLogCount)
            {
                return;
            }

            //Counted as drawn: each record once per repeat
            int total = 0;
            foreach (Log log in logs)
            {
                total += log.count;
            }

            int remove = 0;
            while (total > maxLogCount && remove < logs.Count - 1)
            {
                total -= logs[remove].count;
                remove++;
            }
            if (remove > 0)
            {
                logs.RemoveRange(0, remove);
            }

            //Only one record left and still too many repeats: its count stops at the limit
            if (total > maxLogCount && logs.Count == 1)
            {
                Log only = logs[0];
                only.count = maxLogCount;
                logs[0] = only;
            }
        }
    }

    /// <summary>
    /// A basic container for log details.
    /// </summary>
    struct Log
    {
        public int count;
        public string message;
        public string stackTrace;
        public LogType type;

        /// <summary>
        /// The max string length supported by UnityEngine.GUILayout.Label without triggering this error:
        /// "String too long for TextMeshGenerator. Cutting off characters."
        /// </summary>
        const int maxMessageLength = 16382;

        public bool Equals(Log log)
        {
            return message == log.message && stackTrace == log.stackTrace && type == log.type;
        }

        /// <summary>
        /// Return a truncated message if it exceeds the max message length.
        /// </summary>
        public string GetTruncatedMessage()
        {
            if (string.IsNullOrEmpty(message))
            {
                return message;
            }

            return message.Length <= maxMessageLength ? message : message.Substring(0, maxMessageLength);
        }
    }

    /// <summary>
    /// Alternative to System.Collections.Concurrent.ConcurrentQueue
    /// (It's only available in .NET 4.0 and greater)
    /// </summary>
    /// <remarks>
    /// It's a bit slow (as it uses locks), and only provides a small subset of the interface
    /// Overall, the implementation is intended to be simple & robust
    /// </remarks>
    class ConcurrentQueue<T>
    {
        readonly Queue<T> queue = new Queue<T>();
        readonly object queueLock = new object();

        public void Enqueue(T item)
        {
            lock (queueLock)
            {
                queue.Enqueue(item);
            }
        }

        public bool TryDequeue(out T result)
        {
            lock (queueLock)
            {
                if (queue.Count == 0)
                {
                    result = default(T);
                    return false;
                }

                result = queue.Dequeue();
                return true;
            }
        }
    }
}
