namespace Ticktower.Core.Models;

public enum TimerKind { Countdown, Stopwatch, Interval }

public enum TimerState { Running, Paused, Expired, Acknowledged, Stopped, Pending }

public enum Priority { P1, P2, P3, P4 }

public enum TimerEventType { Start, Stop, Pause, Resume, Lap, Bookmark, Snooze, Expire, Ack, AlertFired }
