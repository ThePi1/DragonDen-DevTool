using System;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace DragonDenDevTool.Utilities;

public sealed class PlayerRotateBlocker(Player player)
{
    // 4.1.6: these were public fields in 0.16.9 (PitchTargetLimit, MyTransformRotation) and are private now.
    private static readonly AccessTools.FieldRef<MovementContext, Vector2> PitchTargetLimitRef =
        AccessTools.FieldRefAccess<MovementContext, Vector2>("_pitchTargetLimit");
    private static readonly AccessTools.FieldRef<MovementContext, Quaternion> MyTransformRotationRef =
        AccessTools.FieldRefAccess<MovementContext, Quaternion>("_myTransformRotation");

    private bool _locked;

    private Vector2 _savedYawLimit;
    private Vector2 _savedPitchLimit;
    private Vector2 _savedPitchTargetLimit;
    private Action<Player> _savedRotationAction;
    private Vector2 _savedRotation;
    private Vector2 _savedPreviousRotation;
    private Quaternion _savedMyTransformRotation;

    public void Lock()
    {
        if (_locked || !player || player.MovementContext == null) return;

        var mc = player.MovementContext;

        _savedYawLimit          = mc.YawLimit;
        _savedPitchLimit        = mc.PitchLimit;
        _savedPitchTargetLimit  = PitchTargetLimitRef(mc);
        _savedRotationAction    = mc.RotationAction;
        _savedRotation          = mc.Rotation;
        _savedPreviousRotation  = mc.PreviousRotation;
        _savedMyTransformRotation = MyTransformRotationRef(mc);

        var yaw   = mc.Yaw;
        var pitch = mc.Pitch;

        mc.SetRotationLimit(new Vector2(yaw, yaw), new Vector2(pitch, pitch));
        mc.SetPitchForce(pitch, pitch);
        mc.SetDirectlyLookRotations(new Vector2(yaw, pitch), new Vector2(yaw, pitch));
        MyTransformRotationRef(mc) = Quaternion.Euler(0f, yaw, 0f);
        mc.UpdateDeltaAngle();

        mc.RotationAction = MovementContext.DefaultRotationFunction;

        _locked = true;
    }

    public void Unlock()
    {
        if (!_locked || !player || player.MovementContext == null) return;

        var mc = player.MovementContext;

        mc.SetRotationLimit(_savedYawLimit, _savedPitchLimit);
        mc.SetPitchSmoothly(_savedPitchTargetLimit);
        mc.SetDirectlyLookRotations(_savedRotation, _savedPreviousRotation);
        MyTransformRotationRef(mc) = _savedMyTransformRotation;
        mc.UpdateDeltaAngle();

        mc.RotationAction = _savedRotationAction ?? MovementContext.DefaultRotationFunction;

        _locked = false;
    }
}