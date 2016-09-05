﻿using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;
using UnityEngine;
using PersistentRotation;

namespace PersistentRotation
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class Main : MonoBehaviour
    {
        public const float threshold = 0.05f;

        public static Main instance { get; private set; }
        private Data data;

        public Vessel activeVessel;

        private void Awake()
        {
            instance = this;

            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onVesselCreate.Add(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameStateSave.Add(OnGameStateSave);
        }
        private void Start()
        {
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

            foreach(Data.PRVessel v in data.PRVessels)
            {
                v.processed = false;
            }

            foreach (Vessel vessel in FlightGlobals.Vessels)
            {
                Data.PRVessel v = data.FindPRVessel(vessel);

                v.processed = true;

                if(v.dynamicReference)
                {
                    if (v.reference == null || ( v.reference.GetType() != typeof(CelestialBody) || v.reference.GetName() != vessel.mainBody.GetName() )) //Main body mode; continuous update of reference to mainBody
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
                        if (!v.momentumModeActive && vessel.Autopilot.Enabled && vessel.IsControllable && v.momentum.magnitude < threshold) //C1
                        {
                            if (v.rotationModeActive == true && v.reference != null) //C2
                            {
                                if (v.reference == v.lastReference)
                                {
                                    PackedRotation(v);
                                }
                            }
                        }
                        else
                        {
                            PackedSpin(v); //NOT CONTROLLABLE
                        }
                    }

                    v.lastActive = false;

                    #endregion
                }
                else
                {
                    #region ### UNPACKED ###
                    //Update Momentum when unpacked
                    if (!v.momentumModeActive && vessel.Autopilot.Enabled && vessel.IsControllable && vessel.angularVelocity.magnitude < threshold) //C1
                    {
                        v.momentum = Vector3.zero;
                    }
                    else
                    {
                        v.momentum = vessel.angularVelocity;
                    }

                    //Apply Momentum to activeVessel using Fly-By-Wire
                    if (v.momentumModeActive && vessel.Autopilot.Enabled) //C1 \ IsControllable
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
                        if (!v.momentumModeActive && vessel.Autopilot.Enabled && vessel.Autopilot.Mode == VesselAutopilot.AutopilotMode.StabilityAssist)
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

            for (int i = 0; i < data.PRVessels.Count; i++)
            {
                if (data.PRVessels[i].processed == false)
                    data.PRVessels.Remove(data.PRVessels[i]);
            }
        }
        private void OnDestroy()
        {
            instance = null;
            //Unbind functions from GameEvents
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.onVesselCreate.Remove(OnVesselCreate);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
        }
        private void OnGameStateSave(ConfigNode config)
        {
            if (data)
            {
                data.Save();
            }
        }

        private void OnVesselCreate(Vessel vessel)
        {
            //Wait for Vessel to be created
            StartCoroutine(LateGenerate(vessel));
        }
        private IEnumerator LateGenerate(Vessel vessel)
        {
            yield return new WaitForEndOfFrame();

            if (vessel) //Check if vessel was already destroyed between last and this frame
            {
                Data.PRVessel v = data.FindPRVessel(vessel);
                v.lastPosition = Vector3.zero;
                v.lastActive = false;
                v.lastReference = null;
                v.lastTransform = vessel.ReferenceTransform;
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
             //Nothing to do here
        }
        private void OnVesselGoOffRails(Vessel vessel)
        {
            Data.PRVessel v = data.FindPRVessel(vessel);
            if (vessel.situation != Vessel.Situations.LANDED || vessel.situation != Vessel.Situations.SPLASHED)
            {
                if (vessel.ActionGroups[KSPActionGroup.SAS] && vessel.IsControllable && !v.momentumModeActive && v.rotationModeActive && v.momentum.magnitude < threshold) //vessel.Autopilot.Enabled does not work at this point!
                {

                    Quaternion shift = Quaternion.Euler(0f, Vector3.Angle(Planetarium.right, v.planetariumRight), 0f);

                    //Set relative rotation if there is a reference
                    if (v.reference != null)
                    {
                        vessel.SetRotation(FromToRotation(shift * v.direction, (v.reference.GetTransform().position - vessel.transform.position).normalized) * (shift * v.rotation));
                    }

                    //Reset momentumModeActive heading
                    vessel.Autopilot.SAS.lockedHeading = vessel.ReferenceTransform.rotation;
                }
                else
                {
                    Vector3 av = v.momentum;
                    Vector3 COM = vessel.findWorldCenterOfMass();
                    Quaternion rotation = vessel.ReferenceTransform.rotation;

                    //Applying force on every part
                    foreach (Part p in vessel.parts)
                    {
                        try
                        {
                            if (p.GetComponent<Rigidbody>() == null) continue;
                            p.GetComponent<Rigidbody>().AddTorque(rotation * av, ForceMode.VelocityChange);
                            p.GetComponent<Rigidbody>().AddForce(Vector3.Cross(rotation * av, (p.GetComponent<Rigidbody>().position - COM)), ForceMode.VelocityChange);
                        }
                        catch (NullReferenceException nre)
                        {
                            Debug.Log("[PR] NullReferenceException in OnVesselGoOffRails: " + nre.Message);
                        }
                    }
                }
            }
        }
        private void PackedSpin(Data.PRVessel v)
        {
            
            if(v.vessel.situation != Vessel.Situations.LANDED || v.vessel.situation != Vessel.Situations.SPLASHED)
                v.vessel.SetRotation(Quaternion.AngleAxis(v.momentum.magnitude * TimeWarp.CurrentRate, v.vessel.ReferenceTransform.rotation * v.momentum) * v.vessel.transform.rotation);
        }
        private void PackedRotation(Data.PRVessel v)
        {
            Quaternion shift = Quaternion.Euler(0f, Vector3.Angle(Planetarium.right, v.planetariumRight), 0f);

            if (v.vessel.situation != Vessel.Situations.LANDED || v.vessel.situation != Vessel.Situations.SPLASHED)
                v.vessel.SetRotation(FromToRotation(shift * v.direction, (v.reference.GetTransform().position - v.vessel.transform.position).normalized) * (shift * v.rotation));
        }
        private void AdjustSAS(Data.PRVessel v)
        {
            if (v.reference != null)
            {
                if (v.lastTransform != null && v.lastPosition != null)
                {
                    Vector3d newPosition = (Vector3d)v.lastTransform.position - v.reference.GetTransform().position;
                    QuaternionD delta = FromToRotation(v.lastPosition, newPosition);
                    QuaternionD adjusted = delta * (QuaternionD)v.vessel.Autopilot.SAS.lockedHeading;
                    v.vessel.Autopilot.SAS.lockedHeading = adjusted;
                }
            }
        }

        public Quaternion FromToRotation(Vector3d fromv, Vector3d tov) //Stock FromToRotation() doesn't work correctly
        {
            Vector3d cross = Vector3d.Cross(fromv, tov);
            double dot = Vector3d.Dot(fromv, tov);
            double wval = dot + Math.Sqrt(fromv.sqrMagnitude * tov.sqrMagnitude);
            double norm = 1.0 / Math.Sqrt(cross.sqrMagnitude + wval * wval);
            return new QuaternionD(cross.x * norm, cross.y * norm, cross.z * norm, wval * norm);
        }
    }
}