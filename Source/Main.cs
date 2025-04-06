﻿using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Main : MonoBehaviour
    {
        public static Main instance { get; private set; }
        private Data data;
        public const float threshold = 0.05f;
        public Vessel activeVessel;

        //int count = 0;

        class RigidBodyMaxAngularVelocitySaveData
        {
            public float OriginalValue;
            public int FrameCounter = 10;

            public RigidBodyMaxAngularVelocitySaveData(float pOriginalValue)
            {
                OriginalValue = pOriginalValue;
            }
        }
        Dictionary<Rigidbody, RigidBodyMaxAngularVelocitySaveData> RigidBodyMaxAngularVelocityPairs = new Dictionary<Rigidbody, RigidBodyMaxAngularVelocitySaveData>();

        /* MONOBEHAVIOUR METHODS */
        private void Awake()
        {
            instance = this;

            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }
        private void Start()
        {
            MechJebWrapper.Initialize();
            RemoteTechWrapper.Initialize();

            activeVessel = FlightGlobals.ActiveVessel;
            data = Data.instance;
        }
        private void FixedUpdate()
        {
            if (activeVessel != FlightGlobals.ActiveVessel)
            {
                activeVessel = FlightGlobals.ActiveVessel;
                Interface.instance.desiredRPMstr = data.FindPRVessel(activeVessel).desiredRPM.ToString();
            }

            foreach (Data.PRVessel v in data.PRVessels)
            {
                v.processed = false;
            }

            if (RigidBodyMaxAngularVelocityPairs.Count > 0)
            {
                List<Rigidbody> toremove = null;
                foreach (KeyValuePair<Rigidbody, RigidBodyMaxAngularVelocitySaveData> kp in RigidBodyMaxAngularVelocityPairs)
                {
                    kp.Value.FrameCounter--;
                    if (kp.Value.FrameCounter == 0)
                    {
                        try
                        {
                            kp.Key.maxAngularVelocity = kp.Value.OriginalValue;
                        }
                        catch
                        {
                        }

                        if (toremove == null)
                            toremove = new List<Rigidbody>();
                        toremove.Add(kp.Key);
                    }
                }

                if (toremove != null)
                {
                    for (int i = 0; i < toremove.Count; ++i)
                    {
                        RigidBodyMaxAngularVelocityPairs.Remove(toremove[i]);
                    }
                }
            }

            #region ### Cycle through all vessels ###
            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                Data.PRVessel v = data.FindPRVessel(vessel);

                v.processed = true;
                if (v.dynamicReference)
                {
                    if (v.reference == null || (v.reference.GetType() != typeof(CelestialBody) || v.reference.GetName() != vessel.mainBody.GetName())) //Main body mode; continuous update of reference to mainBody
                    {
                        Debug.Log("[PR] Updated the reference of " + v.vessel.vesselName + " from " + (v.reference != null ? v.reference.GetName() : "Null") + " to " + vessel.mainBody.name);
                        v.reference = vessel.mainBody;
                        v.direction = (v.reference.GetTransform().position - vessel.transform.position).normalized;
                        v.rotation = vessel.transform.rotation;
                        v.planetariumRight = Planetarium.right;
                        v.lastActive = false;
                    }
                }
                if (vessel.packed)
                {
                    #region ### PACKED ###
                    if (vessel.loaded) //is okay, rotation doesnt need to be persistent when rotating
                    {
                        var currentStabilityMode = GetStabilityMode(vessel);

                        //Debug.Log("[PR] - currentStabilityMode: " + Enum.GetName(typeof(StabilityMode), currentStabilityMode));

                        if (v.storedAngularMomentum.magnitude < threshold && (currentStabilityMode == StabilityMode.PROGRADE || currentStabilityMode == StabilityMode.RETROGRADE))
                        {
                            var rotation = /* Quaternion. */ FromToRotation(
                                (currentStabilityMode == StabilityMode.PROGRADE ? 1 : -1) *
                                vessel.ReferenceTransform.up.normalized, vessel.obt_velocity.normalized);

                            vessel.transform.Rotate(rotation.eulerAngles, Space.World);

                            v.vessel.SetRotation(vessel.transform.rotation);
                        }
                        else
                        if (v.storedAngularMomentum.magnitude < threshold && (currentStabilityMode == StabilityMode.NORMAL || currentStabilityMode == StabilityMode.ANTI_NORMAL))
                        {
#if false
                            var attitude = Quaternion.LookRotation(Vector3d.right, Vector3d.up) * Quaternion.AngleAxis(0, Vector3d.forward);

                            if (currentStabilityMode == StabilityMode.ANTI_NORMAL)
                            {
                                var ea = attitude.eulerAngles;

                                //ea.y += 180;
                                ea.z += 180;
                                attitude = Quaternion.Euler(ea);
                            }

                            v.vessel.SetRotation(attitude);
#endif
                        }
                        else
                            if (v.storedAngularMomentum.magnitude < threshold && (currentStabilityMode == StabilityMode.RADIAL_OUT || currentStabilityMode == StabilityMode.RADIAL_IN))
                        {
                            var rotation = /* Quaternion. */ FromToRotation(
                                (currentStabilityMode == StabilityMode.RADIAL_IN ? 1 : -1) *
                                vessel.ReferenceTransform.up.normalized, FlightGlobals.ActiveVessel.upAxis);

                            vessel.transform.Rotate(rotation.eulerAngles, Space.World);

                            v.vessel.SetRotation(vessel.transform.rotation);
                        }
                        else
                        if (v.storedAngularMomentum.magnitude >= threshold)
                        {
                            if (currentStabilityMode != StabilityMode.AUTOPILOT)
                            {
                                PackedSpin(v);
                            }
                        }
                        else if (currentStabilityMode != StabilityMode.ABSOLUTE)
                        {
                            if (currentStabilityMode == StabilityMode.RELATIVE && v.rotationModeActive && !v.momentumModeActive && v.storedAngularMomentum.magnitude < threshold)
                            {
                                if (v.rotationModeActive == true && v.reference != null)
                                {
                                    if (v.reference == v.lastReference)
                                    {
                                        PackedRotation(v);
                                    }
                                }
                            }
                            else
                            {
                                PackedSpin(v);
                            }
                        }
                    }

                    v.lastActive = false;

#endregion
                }
                else
                {
#region ### UNPACKED ###
                    //Did this vessel just go off rails?
                    if (v.GoingOffRailsFrameCounter != -1)
                    {
                        --v.GoingOffRailsFrameCounter;
                        if (v.GoingOffRailsFrameCounter == 0)
                        {
                            ApplyMomentumNow(v);
                            v.GoingOffRailsFrameCounter = -1;
                        }
                    }
                    else
                    {
                        //Update Momentum when unpacked
                        if (GetStabilityMode(vessel) != StabilityMode.OFF && !v.momentumModeActive && vessel.angularVelocity.magnitude < threshold) //C1
                        {
                            v.storedAngularMomentum = Vector3.zero;
                        }
                        else
                        {
                            v.storedAngularMomentum = vessel.angularMomentum;
                            //KSPLog.print(string.Format("SAVE angular vel: {0}, Momentum: {1}, MOI: {2}", vessel.angularVelocity, vessel.angularMomentum, vessel.MOI));
                        }
                    }

                    //Update mjMode when unpacked
                    v.mjMode = MechJebWrapper.GetMode(vessel);
                    v.rtMode = RemoteTechWrapper.GetMode(vessel);

                    //Apply Momentum to activeVessel using Fly-By-Wire
                    if (GetStabilityMode(vessel) == StabilityMode.RELATIVE && v.momentumModeActive) //C1 \ IsControllable
                    {
                        float desiredRPM = (vessel.angularVelocity.magnitude * 60f * (1f / Time.fixedDeltaTime)) / 360f;
                        if (v.desiredRPM >= 0)
                        {
                            vessel.ctrlState.roll = Mathf.Clamp((v.desiredRPM - desiredRPM), -1f, +1f);
                        }
                        else
                        {
                            vessel.ctrlState.roll = -Mathf.Clamp((-v.desiredRPM - desiredRPM), -1f, +1f);
                        }
                    }

                    //Update rotation
                    v.rotation = vessel.transform.rotation;

                    v.planetariumRight = Planetarium.right;

                    //Adjust SAS for Relative Rotation
                    if (v.rotationModeActive && v.reference != null) //C2
                    {
                        //Update direction
                        v.direction = (v.reference.GetTransform().position - vessel.transform.position).normalized;
                        if (GetStabilityMode(vessel) == StabilityMode.RELATIVE && !v.momentumModeActive)
                        {
                            if (v.lastActive && v.reference == v.lastReference)
                            {
                                AdjustSAS(v);
                            }
                            v.lastActive = true;
                        }
                        else
                        {
                            v.lastActive = false;
                        }

                        v.lastPosition = (Vector3d)v.lastTransform.position - v.reference.GetTransform().position;
                    }
                    else
                    {
                        v.direction = Vector3.zero;
                        v.lastPosition = Vector3.zero;
                        v.lastActive = false;
                    }
#endregion
                }

                v.lastTransform = vessel.ReferenceTransform;
                v.lastReference = v.reference;
            }
#endregion

            data.PRVessels.RemoveAll(v => v.processed == false);
        }

        private void OnDestroy()
        {
            instance = null;
            //Unbind functions from GameEvents
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);

            RigidBodyMaxAngularVelocityPairs.Clear();
        }

        /* EVENT METHODS */
        private void OnGameStateSave(ConfigNode config)
        {
            if (data)
            {
                data.Save();
            }
        }
        private void OnVesselWillDestroy(Vessel vessel)
        {
            Debug.Log("[PR] Deleting " + vessel.vesselName + " as reference.");

            foreach (Vessel _vessel in FlightGlobals.Vessels)
            {
                Data.PRVessel v = data.FindPRVessel(_vessel);
                if (!object.ReferenceEquals(_vessel, vessel))
                {
                    if (object.ReferenceEquals(vessel, v.reference))
                    {
                        v.reference = null;
                    }
                }
            }
        }
        private void OnVesselGoOnRails(Vessel vessel)
        {
            // MOI needs to be saved before packing / going on rails, as the value won't be available after that
            data.FindPRVessel(vessel).storedMOI = vessel.MOI;

            //DEBUG
            //KSPLog.print(string.Format("GoOnRails angvel: {0}, stored: {1}", vessel.angularVelocityD, v.storedAngularMomentum));
        }
        private void OnVesselGoOffRails(Vessel vessel)
        {
            Data.PRVessel v = data.FindPRVessel(vessel);
            if (vessel.situation != Vessel.Situations.LANDED && vessel.situation != Vessel.Situations.SPLASHED && vessel.situation != Vessel.Situations.PRELAUNCH)
            {
                if (v.rotationModeActive && v.storedAngularMomentum.magnitude > threshold)
                {
                    if (GetStabilityMode(vessel) != StabilityMode.AUTOPILOT)
                    {
                        ApplyMomentumDelayed(v);
                    }
                }
                else if (GetStabilityMode(vessel) != StabilityMode.ABSOLUTE)
                {
                    if (GetStabilityMode(vessel) == StabilityMode.RELATIVE && !v.momentumModeActive)
                    {

                        Quaternion shift = Quaternion.Euler(0f, Vector3.Angle(Planetarium.right, v.planetariumRight), 0f);

                        //Set relative rotation if there is a reference
                        if (v.reference != null)
                        {
                            vessel.SetRotation(FromToRotation(shift * v.direction, (v.reference.GetTransform().position - vessel.transform.position).normalized) * (shift * v.rotation), true);
                        }

                        //Reset momentumModeActive heading
                        vessel.Autopilot.SAS.lockedRotation = vessel.ReferenceTransform.rotation;
                    }
                    else
                    {
                        ApplyMomentumDelayed(v);
                    }
                }
            }
        }

        /* PRIVATE METHODS */
        private void PackedSpin(Data.PRVessel v)
        {
            Vector3 _angularVelocity = Vector3.zero;

            _angularVelocity.x = v.storedMOI.x == 0f ? 0f : v.storedAngularMomentum.x / v.storedMOI.x;
            _angularVelocity.y = v.storedMOI.y == 0f ? 0f : v.storedAngularMomentum.y / v.storedMOI.y;
            _angularVelocity.z = v.storedMOI.z == 0f ? 0f : v.storedAngularMomentum.z / v.storedMOI.z;

            if (v.vessel.situation != Vessel.Situations.LANDED && v.vessel.situation != Vessel.Situations.SPLASHED && v.vessel.situation != Vessel.Situations.PRELAUNCH)
                v.vessel.SetRotation(Quaternion.AngleAxis(_angularVelocity.magnitude * TimeWarp.CurrentRate, v.vessel.ReferenceTransform.rotation * _angularVelocity) * v.vessel.transform.rotation, true);
        }
        private void PackedRotation(Data.PRVessel v)
        {
            Quaternion shift = Quaternion.Euler(0f, Vector3.Angle(Planetarium.right, v.planetariumRight), 0f);
            if (v.vessel.situation != Vessel.Situations.LANDED && v.vessel.situation != Vessel.Situations.SPLASHED && v.vessel.situation != Vessel.Situations.PRELAUNCH)
                v.vessel.SetRotation(FromToRotation(shift * v.direction, (v.reference.GetTransform().position - v.vessel.transform.position).normalized) * (shift * v.rotation), true);
        }
        private void AdjustSAS(Data.PRVessel v)
        {
            if (v.reference != null)
            {
                if (v.lastTransform != null && v.lastPosition != null)
                {
                    Vector3d newPosition = (Vector3d)v.lastTransform.position - v.reference.GetTransform().position;
                    QuaternionD delta = FromToRotation(v.lastPosition, newPosition);
                    QuaternionD adjusted = delta * (QuaternionD)v.vessel.Autopilot.SAS.lockedRotation;
                    v.vessel.Autopilot.SAS.lockedRotation = adjusted;
                }
            }
        }
        private void ApplyMomentumDelayed(Data.PRVessel v)
        {
            //Wait a few ticks to apply these.
            //Note that if the vessel immediately went back on rails,
            //these values would be updated when it came off again and all
            //would be well.
            v.GoingOffRailsFrameCounter = 3;
            v.GoingOffRailsAngularMomentum = v.storedAngularMomentum;
        }
        private void ApplyMomentumNow(Data.PRVessel v)
        {
            Vector3d angularmomentum = v.GoingOffRailsAngularMomentum;

            Vector3d MOI = v.vessel.MOI;
            //These shouldn't happen
            if (MOI.x == 0d) MOI.x = 0.001d;
            if (MOI.y == 0d) MOI.y = 0.001d;
            if (MOI.z == 0d) MOI.z = 0.001d;

            Vector3d angularvelocity = Vector3d.zero;
            angularvelocity.x = angularmomentum.x / MOI.x;
            angularvelocity.y = angularmomentum.y / MOI.y;
            angularvelocity.z = angularmomentum.z / MOI.z;
            //Vector3d av = v.GoingOffRailsAngularMomentum;

            Vector3d COM = v.vessel.CoMD;
            Quaternion rotation = v.vessel.ReferenceTransform.rotation;
            Vector3d av_by_rotation = rotation * angularvelocity;

            //KSPLog.print(string.Format("Apply angular vel: {0}, MOI: {1}, momentum: {3}", angularvelocity, MOI, angularmomentum));

            //Applying force on every part
            foreach (Part p in v.vessel.parts)
            {
                if (!p.GetComponent<Rigidbody>()) continue;
                if (p.mass == 0f) continue;
                Rigidbody partrigidbody = p.GetComponent<Rigidbody>();

                //Store the Rigidbody's max angular velocity for later restoration.
                if (!RigidBodyMaxAngularVelocityPairs.ContainsKey(partrigidbody))
                    RigidBodyMaxAngularVelocityPairs[partrigidbody] = new RigidBodyMaxAngularVelocitySaveData(partrigidbody.maxAngularVelocity);
                RigidBodyMaxAngularVelocityPairs[partrigidbody].FrameCounter = 10;
                partrigidbody.maxAngularVelocity *= 10f;

                partrigidbody.AddTorque(av_by_rotation, ForceMode.VelocityChange);
                partrigidbody.AddForce(Vector3d.Cross(av_by_rotation, (Vector3d)p.WCoM - COM), ForceMode.VelocityChange);
            }
        }

        private Quaternion FromToRotation2(Vector3d fromv, Vector3d tov) //Stock FromToRotation() doesn't work correctly
        {
            double dot = Vector3d.Dot(fromv, tov);


            if (dot > 0.999999 || dot < -0.999999)
                return Quaternion.identity; //  new Quaternion(0, 0, 0, 0);

            Debug.Log("[PR] - fromv = " + fromv.ToString());
            Debug.Log("[PR] - tov = " + tov.ToString());
            Debug.Log("[PR] - Vector3d.Dot(fromv, tov) = " + dot);
            Vector3d cross = Vector3d.Cross(fromv, tov);
            Debug.Log("[PR] - Vector3d.Cross(fromv, tov) = " + cross.ToString());

            //double wval = (1 - dot) + Math.Sqrt((fromv.magnitude * fromv.magnitude) * (tov.magnitude * tov.magnitude));
            //double norm = 1.0 / Math.Sqrt((cross.x * cross.x) + (cross.y * cross.y) + (cross.z * cross.z) + (wval * wval));
            //var result = new QuaternionD((float)(cross.x * norm), (float)(cross.y * norm), (float)(cross.z * norm), (float)(wval * norm));

            double wval = (1 - dot) + Math.Sqrt(fromv.sqrMagnitude * tov.sqrMagnitude);
            //double norm = 1.0 / Math.Sqrt(cross.sqrMagnitude + wval * wval);
            var result = new QuaternionD(cross.x, cross.y, cross.z, wval);

            return result;
        }

        /* UTILITY METHODS */
        private Quaternion FromToRotation(Vector3d fromv, Vector3d tov) //Stock FromToRotation() doesn't work correctly
        {
            Vector3d cross = Vector3d.Cross(fromv, tov);
            double dot = Vector3d.Dot(fromv, tov);
            //Debug.Log("[PR] - Vector3d.Dot(fromv, tov) = " + dot);
            double wval = dot + Math.Sqrt(fromv.sqrMagnitude * tov.sqrMagnitude);
            double norm = 1.0 / Math.Sqrt(cross.sqrMagnitude + wval * wval);
            return new QuaternionD(cross.x * norm, cross.y * norm, cross.z * norm, wval * norm);
        }
        private enum StabilityMode
        {
            OFF,
            ABSOLUTE,
            RELATIVE,
            AUTOPILOT, //When MechJeb controls the vessel, this will be removed when MechJeb waits to enter warp until momentum is zero.
            PROGRADE,
            RETROGRADE,
            NORMAL,
            ANTI_NORMAL,
            RADIAL_OUT,
            RADIAL_IN
        }
        private StabilityMode GetStabilityMode(Vessel vessel)
        {
            if (vessel.IsControllable == false || RemoteTechWrapper.Controllable(vessel) == false)
                return StabilityMode.OFF; /* Vessel is uncontrollable */
            else if (RemoteTechWrapper.GetMode(vessel) != RemoteTechWrapper.ACFlightMode.Off)
                return StabilityMode.ABSOLUTE;
            else if (MechJebWrapper.Active(vessel))
            {
                return StabilityMode.AUTOPILOT; /* MechJeb is commanding the vessel */
            }
            else if (vessel.ActionGroups[KSPActionGroup.SAS] && data.FindPRVessel(vessel).mjMode == MechJebWrapper.SATarget.OFF && MechJebWrapper.GetMode(vessel) == MechJebWrapper.SATarget.OFF)
            {
                /* Only stock SAS is enabled */
                switch (vessel.Autopilot.Mode)
                {
                    case VesselAutopilot.AutopilotMode.StabilityAssist: return StabilityMode.RELATIVE;
                    case VesselAutopilot.AutopilotMode.Prograde: return StabilityMode.PROGRADE;
                    case VesselAutopilot.AutopilotMode.Retrograde: return StabilityMode.RETROGRADE;

#if true
                    case VesselAutopilot.AutopilotMode.Normal: return StabilityMode.NORMAL;
                    case VesselAutopilot.AutopilotMode.Antinormal: return StabilityMode.ANTI_NORMAL;
#endif
                    case VesselAutopilot.AutopilotMode.RadialOut: return StabilityMode.RADIAL_OUT;
                    case VesselAutopilot.AutopilotMode.RadialIn: return StabilityMode.RADIAL_IN;

                }
                return StabilityMode.ABSOLUTE;
            }
            else if (!vessel.ActionGroups[KSPActionGroup.SAS] && (data.FindPRVessel(vessel).mjMode != MechJebWrapper.SATarget.OFF || MechJebWrapper.GetMode(vessel) != MechJebWrapper.SATarget.OFF))
                return StabilityMode.ABSOLUTE; /* Only SmartA.S.S. is enabled */
            else if (!vessel.ActionGroups[KSPActionGroup.SAS] && data.FindPRVessel(vessel).mjMode == MechJebWrapper.SATarget.OFF && MechJebWrapper.GetMode(vessel) == MechJebWrapper.SATarget.OFF)
                return StabilityMode.OFF; /* Nothing is enabled */
            else
                return StabilityMode.OFF;
        }
    }
}