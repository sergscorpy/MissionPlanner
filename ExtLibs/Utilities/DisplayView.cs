﻿using org.mariuszgromada.math.mxparser.mathcollection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace MissionPlanner.Utilities
{
    [Serializable]
    public enum DisplayNames
    {
        Basic,
        Advanced,
        Custom
    }

    public enum SeverityLevel
    {
        Emergency,
        Alert,
        Critical,
        Error,
        Warning,
        Notice,
        Info,
        Debug
    }

    [Serializable]
    public class DisplayView
    {
        public bool displayRTKInject { get; set; } = true;
        public bool displayGPSOrder { get; set; } = true;
        public bool displayHWIDs { get; set; } = true;
        public bool displayADSB { get; set; } = true;
        public DisplayNames displayName { get; set; }

        //MainV2 buttons
        public Boolean displaySimulation { get; set; }
        public Boolean displayTerminal { get; set; }
        public Boolean displayDonate { get; set; }
        public Boolean displayHelp { get; set; }

        //flight Data view
        public Boolean displayAnenometer { get; set; }
        public Boolean displayQuickTab { get; set; }
        public Boolean displayPreFlightTab { get; set; }
        public Boolean displayAdvActionsTab { get; set; }
        public Boolean displayAdvCopterTab { get; set; }
        public Boolean displayAdvPlaneTab { get; set; }
        public Boolean displaySimpleActionsTab { get; set; }
        public Boolean displayGaugesTab { get; set; }
        public Boolean displayStatusTab { get; set; }
        public Boolean displayServoTab { get; set; }
        public Boolean displayScriptsTab { get; set; }
        public Boolean displayTelemetryTab { get; set; }
        public Boolean displayDataflashTab { get; set; }
        public Boolean displayMessagesTab { get; set; }
        public Boolean displayTransponderTab { get; set; }
        public Boolean displayAuxFunctionTab { get; set; }
        public Boolean displayPayloadTab { get; set; }

        //flight plan
        public Boolean displayRallyPointsMenu { get; set; }
        public Boolean displayGeoFenceMenu { get; set; }
        public Boolean displaySplineCircleAutoWp { get; set; }
        public Boolean displayTextAutoWp { get; set; }
        public Boolean displayCircleSurveyAutoWp { get; set; }
        public Boolean displayPoiMenu { get; set; }
        public Boolean displayTrackerHomeMenu { get; set; }
        public Boolean displayCheckHeightBox { get; set; }
        public Boolean displayPluginAutoWp { get; set; }

        //initial setup
        public Boolean displayInstallFirmware { get; set; }
        public Boolean displayWizard { get; set; }
        public Boolean displayFrameType { get; set; }
        public Boolean displayAccelCalibration { get; set; }
        public Boolean displayCompassConfiguration { get; set; }
        public Boolean displayRadioCalibration { get; set; }
        public Boolean displayEscCalibration { get; set; }
        public Boolean displayFlightModes { get; set; }
        public Boolean displayFailSafe { get; set; }
        public Boolean displaySikRadio { get; set; }
        public Boolean displayBattMonitor { get; set; }
        public Boolean displayCAN { get; set; }
        public Boolean displayCompassMotorCalib { get; set; }
        public Boolean displayRangeFinder { get; set; }
        public Boolean displayAirSpeed { get; set; }
        public Boolean displayPx4Flow { get; set; }
        public Boolean displayOpticalFlow { get; set; }
        public Boolean displayOsd { get; set; }
        public Boolean displayCameraGimbal { get; set; }
        public Boolean displayMotorTest { get; set; }
        public Boolean displayBluetooth { get; set; }
        public Boolean displayParachute { get; set; }
        public Boolean displayEsp { get; set; }
        public Boolean displayAntennaTracker { get; set; }
        public Boolean displaySerialPorts { get; set; }


        //config tuning
        public Boolean displayGeoFence { get; set; }
        public Boolean displayBasicTuning { get; set; }
        public Boolean displayExtendedTuning { get; set; }
        public Boolean displayStandardParams { get; set; }
        public Boolean displayAdvancedParams { get; set; }
        public Boolean displayMavFTP { get; set; }
        public Boolean displayFullParamList { get; set; }
        public Boolean displayFullParamTree { get; set; }
        public Boolean displayParamCommitButton { get; set; }
        public Boolean displayBaudCMB { get; set; }
        public Boolean displaySerialPortCMB { get; set; }
        public Boolean standardFlightModesOnly { get; set; }
        public Boolean autoHideMenuForce { get; set; }
        public Boolean displayInitialParams { get; set; }
        public bool isAdvancedMode { get; set; }
        public Boolean displayREPL { get; set; }
        public bool displayServoOutput { get; set; } = true;
        public bool displayJoystick { get; set; } = true;
        public bool displayOSD { get; set; } = true;
        public bool displayUserParam { get; set; } = true;
        public bool displayPlannerSettings { get; set; } = true;
        public bool displayFFTSetup { get; set; } = true;
        public bool displayPreFlightTabEdit { get; set; } = true;
        public bool displayPlannerLayout { get; set; } = true;

        public bool lockQuickView { get; set; } = false;

        public DisplayView()
        {
            // default to basic.
            //also when a new field is added/created this defines the template for missing options
            displayName = DisplayNames.Basic;

            //MainV2 buttons
            displaySimulation = false;
            displayDonate = true;
            displayHelp = true;

            //flight Data view
            displayAnenometer = true;
            displayQuickTab = true;
            displayPreFlightTab = true;
            displayAdvActionsTab = false;
            displayAdvCopterTab = true;
            displayAdvPlaneTab = true;
            displaySimpleActionsTab = true;
            displayGaugesTab = true;
            displayStatusTab = false;
            displayServoTab = false;
            displayScriptsTab = false;
            displayTelemetryTab = true;
            displayDataflashTab = true;
            displayMessagesTab = true;
            displayTransponderTab = true;
            displayAuxFunctionTab = true;
            displayPayloadTab = true;

            //flight plan
            displayRallyPointsMenu = true;
            displayGeoFenceMenu = true;
            displaySplineCircleAutoWp = true;
            displayTextAutoWp = true;
            displayCircleSurveyAutoWp = true;
            displayPoiMenu = true;
            displayTrackerHomeMenu = true;
            displayCheckHeightBox = true;
            displayPluginAutoWp = true;

            //initial setup
            displayInstallFirmware = true;
            displayInitialParams = true;
            displayWizard = true;
            displayFrameType = true;
            displayAccelCalibration = true;
            displayCompassConfiguration = true;
            displayRadioCalibration = true;
            displayServoOutput = true;
            displayEscCalibration = true;
            displayFlightModes = true;
            displayFailSafe = true;
            displaySikRadio = true;
            displayBattMonitor = true;
            displayCAN = true;
            displayCompassMotorCalib = true;
            displayRangeFinder = true;
            displayAirSpeed = true;
            displayPx4Flow = true;
            displayOpticalFlow = true;
            displayOsd = true;
            displayCameraGimbal = true;
            displayMotorTest = true;
            displayBluetooth = true;
            displayParachute = true;
            displayEsp = true;
            displayAntennaTracker = true;
            displayRTKInject = true;
            displayJoystick = true;
            displaySerialPorts = true;
            displayREPL = true;
            displayTerminal = true;

            //config tuning
            displayGeoFence = true;
            displayBasicTuning = true;
            displayExtendedTuning = true;
            displayStandardParams = false;
            displayAdvancedParams = false;
            displayMavFTP = true;
            displayFullParamList = true;
            displayFullParamTree = true;
            displayParamCommitButton = false;
            displayBaudCMB = true;
            standardFlightModesOnly = false;
            displaySerialPortCMB = true;
            autoHideMenuForce = false;
            displayOSD = true;
            isAdvancedMode = false;
        }
    }
    public static class DisplayViewExtensions
    {
        public static bool TryParse(string value, out DisplayView result)
        {
            result = new DisplayView();
            var serializer = new XmlSerializer(result.GetType());

            using (TextReader reader = new StringReader(value))
            {
                if (!value.StartsWith("{"))
                {
                    try
                    {
                        result = (DisplayView) serializer.Deserialize(reader);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }

                try
                {
                    result = value.FromJSON<DisplayView>();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
        public static string ConvertToString(this DisplayView v)
        {
            return v.ToJSON();

            XmlSerializer xmlSerializer = new XmlSerializer(v.GetType());
            using (StringWriter textWriter = new StringWriter())
            {
                xmlSerializer.Serialize(textWriter, v);
                return textWriter.ToString();
            }
        }
        public static DisplayView Basic(this DisplayView v)
        {
            return new DisplayView()
            {
                displayName = DisplayNames.Basic,
                //MainV2 buttons
                displaySimulation = true,
                displayDonate = true,
                displayHelp = true,

                //flight Data view
                displayAnenometer = true,
                displayQuickTab = true,
                displayPreFlightTab = true,
                displayAdvActionsTab = false,
                displayAdvCopterTab = true,
                displayAdvPlaneTab = true,
                displaySimpleActionsTab = true,
                displayGaugesTab = true,
                displayStatusTab = false,
                displayServoTab = false,
                displayScriptsTab = false,
                displayTelemetryTab = true,
                displayDataflashTab = true,
                displayMessagesTab = true,
                displayTransponderTab = true,
                displayAuxFunctionTab = true,
                displayPayloadTab = true,

                //flight plan
                displayRallyPointsMenu = true,
                displayGeoFenceMenu = true,
                displaySplineCircleAutoWp = true,
                displayTextAutoWp = true,
                displayCircleSurveyAutoWp = true,
                displayPoiMenu = true,
                displayTrackerHomeMenu = true,
                displayCheckHeightBox = true,
                displayPluginAutoWp = true,

                //initial setup
                displayInstallFirmware = true,
                displayWizard = true,
                displayFrameType = true,
                displayAccelCalibration = true,
                displayCompassConfiguration = true,
                displayRadioCalibration = true,
                displayServoOutput = true,
                displayEscCalibration = true,
                displayFlightModes = true,
                displayFailSafe = true,
                displaySikRadio = true,
                displayBattMonitor = true,
                displayCAN = true,
                displayCompassMotorCalib = true,
                displayRangeFinder = true,
                displayAirSpeed = true,
                displayPx4Flow = true,
                displayOpticalFlow = true,
                displayOsd = true,
                displayCameraGimbal = true,
                displayMotorTest = true,
                displayBluetooth = true,
                displayParachute = true,
                displayEsp = true,
                displayAntennaTracker = true,
                displayRTKInject = true,
                displayJoystick = true,
                displayREPL = true,
                displayTerminal = false,

                //config tuning
                displayGeoFence = true,
                displayBasicTuning = true,
                displayExtendedTuning = true,
                displayStandardParams = false,
                displayAdvancedParams = false,
                displayMavFTP = true,
                displayFullParamList = true,
                displayFullParamTree = true,
                displayParamCommitButton = false,
                displayBaudCMB = true,
                displaySerialPortCMB = true,
                displayOSD = true,
                standardFlightModesOnly = false,
                autoHideMenuForce = false,
                isAdvancedMode = false
            };
        }
        public static DisplayView Advanced(this DisplayView v)
        {
            return new DisplayView()
            {
                displayName = DisplayNames.Advanced,
                //MainV2 buttons
                displaySimulation = true,
                displayDonate = true,
                displayHelp = true,

                //flight Data view
                displayAnenometer = true,
                displayQuickTab = true,
                displayPreFlightTab = true,
                displayAdvActionsTab = true,
                displayAdvCopterTab = true,
                displayAdvPlaneTab = true,
                displaySimpleActionsTab = false,
                displayGaugesTab = true,
                displayStatusTab = true,
                displayServoTab = true,
                displayScriptsTab = true,
                displayTelemetryTab = true,
                displayDataflashTab = true,
                displayMessagesTab = true,
                displayTransponderTab = true,
                displayAuxFunctionTab = true,
                displayPayloadTab = true,

                //flight plan
                displayRallyPointsMenu = true,
                displayGeoFenceMenu = true,
                displaySplineCircleAutoWp = true,
                displayTextAutoWp = true,
                displayCircleSurveyAutoWp = true,
                displayPoiMenu = true,
                displayTrackerHomeMenu = true,
                displayCheckHeightBox = true,
                displayPluginAutoWp = true,

                //initial setup
                displayInstallFirmware = true,
                displayWizard = true,
                displayFrameType = true,
                displayAccelCalibration = true,
                displayCompassConfiguration = true,
                displayRadioCalibration = true,
                displayServoOutput = true,
                displayEscCalibration = true,
                displayFlightModes = true,
                displayFailSafe = true,
                displaySikRadio = true,
                displayBattMonitor = true,
                displayCAN = true,
                displayCompassMotorCalib = true,
                displayRangeFinder = true,
                displayAirSpeed = true,
                displayPx4Flow = true,
                displayOpticalFlow = true,
                displayOsd = true,
                displayCameraGimbal = true,
                displayMotorTest = true,
                displayBluetooth = true,
                displayParachute = true,
                displayEsp = true,
                displayAntennaTracker = true,
                displayRTKInject = true,
                displayJoystick = true,
                displayREPL = true,
                displayTerminal = true,

                //config tuning
                displayGeoFence = true,
                displayBasicTuning = true,
                displayExtendedTuning = true,
                displayStandardParams = false,
                displayAdvancedParams = false,
                displayMavFTP = true,
                displayFullParamList = true,
                displayFullParamTree = true,
                displayParamCommitButton = false,
                displayBaudCMB = true,
                displaySerialPortCMB = true,
                standardFlightModesOnly = false,
                displayOSD = true,
                autoHideMenuForce = false,
                isAdvancedMode = true
            };
        }

        public static string custompath = Settings.GetRunningDirectory() + Path.DirectorySeparatorChar + "custom.displayview";

        public static DisplayView Custom(this DisplayView v)
        {
            var result = new DisplayView().Advanced();

            if (File.Exists(custompath) && TryParse(File.ReadAllText(custompath), out result))
            {
                result.displayName = DisplayNames.Custom;
                return result;
            }

            return result;
        }
    }
}