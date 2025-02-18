﻿using System;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class RegExFindComponent : ItemComponent
    {
        private static readonly TimeSpan timeout = TimeSpan.FromSeconds(Timing.Step);

        private string expression;

        private string receivedSignal;
        private string previousReceivedSignal;

        private bool previousResult;
        private GroupCollection previousGroups;

        private Regex regex;

        private bool nonContinuousOutputSent;

        private int maxOutputLength;
        [Editable, Serialize(200, IsPropertySaveable.No, description: "The maximum length of the output string. Warning: Large values can lead to large memory usage or networking issues.")]
        public int MaxOutputLength
        {
            get { return maxOutputLength; }
            set
            {
                maxOutputLength = Math.Max(value, 0);
            }
        }

        private string output;

        [InGameEditable, Serialize("1", IsPropertySaveable.Yes, description: "The signal this item outputs when the received signal matches the regular expression.", alwaysUseInstanceValues: true)]
        public string Output 
        {
            get { return output; }
            set
            {
                if (value == null) { return; }
                output = value;
                if (output.Length > MaxOutputLength && (item.Submarine == null || !item.Submarine.Loading))
                {
                    output = output.Substring(0, MaxOutputLength);
                }
            }
        }

        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "Should the component output a value of a capture group instead of a constant signal.", alwaysUseInstanceValues: true)]
        public bool UseCaptureGroup { get; set; }

        [InGameEditable, Serialize("0", IsPropertySaveable.Yes, description: "The signal this item outputs when the received signal does not match the regular expression.", alwaysUseInstanceValues: true)]
        public string FalseOutput { get; set; }

        [InGameEditable, Serialize(true, IsPropertySaveable.Yes, description: "Should the component keep sending the output even after it stops receiving a signal, or only send an output when it receives a signal.", alwaysUseInstanceValues: true)]
        public bool ContinuousOutput { get; set; }

        [InGameEditable, Serialize("", IsPropertySaveable.Yes, description: "The regular expression used to check the incoming signals.", alwaysUseInstanceValues: true)]
        public string Expression
        {
            get { return expression; }
            set 
            {
                if (expression == value) return;
                expression = value;
                previousReceivedSignal = "";

                try
                {
                    regex = new Regex(
                        @expression,
                        options: RegexOptions.None,
                        matchTimeout: timeout);
                }

                catch
                {
                    return;
                }
            }
        }

        public RegExFindComponent(Item item, ContentXElement element)
            : base(item, element)
        {
            nonContinuousOutputSent = true;
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (string.IsNullOrWhiteSpace(expression) || regex == null) { return; }
            if (!ContinuousOutput && nonContinuousOutputSent) { return; }

            if (receivedSignal != previousReceivedSignal && receivedSignal != null)
            {
                try
                {
                    Match match = regex.Match(receivedSignal);
                    previousResult = match.Success;
                    previousGroups = UseCaptureGroup && previousResult ? match.Groups : null;
                    previousReceivedSignal = receivedSignal;
                }
                catch (Exception e)
                {
                    item.SendSignal(
                        e is RegexMatchTimeoutException
                            ? "TIMEOUT"
                            : "ERROR",
                        "signal_out");
                    previousResult = false;
                    return;
                }
            }

            string signalOut;
            if (previousResult)
            {
                if (UseCaptureGroup)
                {
                    if (previousGroups != null && previousGroups.TryGetValue(Output, out Group group))
                    {
                        signalOut = group.Value;
                    }
                    else
                    {
                        signalOut = FalseOutput;
                    }
                }
                else
                {
                    signalOut = Output;
                }
            }
            else
            {
                signalOut = FalseOutput;
            }

            if (ContinuousOutput)
            {
                if (!string.IsNullOrEmpty(signalOut)) { item.SendSignal(signalOut, "signal_out"); }
            }
            else
            {
                if (!string.IsNullOrEmpty(signalOut)) { item.SendSignal(signalOut, "signal_out"); }
                nonContinuousOutputSent = true;
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    receivedSignal = signal.value;
                    nonContinuousOutputSent = false;
                    break;
                case "set_output":
                    Output = signal.value;
                    break;
            }
        }
    }
}
