using Smotrel.Enums;
using System.Windows;

namespace Smotrel.Events
{
    public class PlayerStateChangedEventArgs : RoutedEventArgs
    {
        public PlayerState OldState { get; }
        public PlayerState NewState { get; }

        public PlayerStateChangedEventArgs(
            RoutedEvent routedEvent,
            PlayerState oldState,
            PlayerState newState) : base(routedEvent)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public class VolumeChangedEventArgs : RoutedEventArgs
    {
        public double OldVolume { get; }
        public double NewVolume { get; }

        public VolumeChangedEventArgs(
            RoutedEvent routedEvent,
            double oldVolume,
            double newVolume) : base(routedEvent)
        {
            OldVolume = oldVolume;
            NewVolume = newVolume;
        }
    }

    public class VideoWindowStateChangedEventArgs : RoutedEventArgs
    {
        public VideoWindowState OldState { get; }
        public VideoWindowState NewState { get; }

        public VideoWindowStateChangedEventArgs(
            RoutedEvent routedEvent,
            VideoWindowState oldState,
            VideoWindowState newState) : base(routedEvent)
        {
            OldState = oldState;
            NewState = newState;
        }
    }
}
