namespace Trinity.Eclipse;

// Anims and the thug hold, tuned in game. They only work as a set, so they aren't settings.
internal static class Rig
{
    // Both arms up over his head, from the ledge climb. For cars and objects.
    public const string HoldAnim = "Dangle_Climb_Up_Over_128";
    public const string ThrowAnim = "Thug_Grab_Player_Strike_Out_Throw";
    // The interrogation grab and hold. The thug plays his own side of the same pair.
    public const string GrabAnim = "Ledge_In";
    public const string NeckAnim = "Ledge_Idle";
    public const string ThugGrabSide = "Ledge_In";
    public const string ThugHoldSide = "Ledge_Idle";
    // The dive bomb landing, for coming out of a ground slam.
    public const string SlamLandAnim = "Glide_Boost_Land";
    // The glide landing skid, for stopping a super run.
    public const string SkidAnim = "Glide_Land_Skid";
    // Fingers at his cowl, from the radio set. For heat vision while hovering.
    public const string BeamAnim = "Stand_Idle_Radio_BM";

    public const string HoverPose = "standing";
    public const string FastPose = "line_launcher_dive";
    // The legs loop twitches his shoulders each time round, so it's held on one frame.
    public const string HoverLegs = "Grapple_Idle";
    public const string HoverUpper = "Stand_Idle";

    // Where the thug hangs from his hand, in metres and degrees.
    public const bool GripLeftHand = false;
    public const float GripAhead = 0.15f;
    public const float GripSide = 0.1f;
    public const float GripDrop = 0f;
    public const float GripPitch = 0f;
    public const float GripRoll = -5f;
    public const float GripYaw = 150f;
    public const float GripThugPitch = 5f;
}
