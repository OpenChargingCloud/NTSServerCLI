/*
 * Copyright (c) 2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of NTSServerCLI <https://github.com/OpenChargingCloud/NTSServerCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

using cloud.charging.open.NTSServer.CommandLine;

#endregion

namespace cloud.charging.open.NTSServer
{

    /// <summary>
    /// One NTS server, with its web interface and a prompt, until 'quit',
    /// Ctrl+C or SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches and
    /// the words -h explains them with, why it could not be set up or could not
    /// start, what goes into the certificate store, the banner and the prompt.
    /// What is left here is the time server's: the switches that say for one
    /// start what the "ntsServer" section says for every start, and what the
    /// banner says of NTS-KE, NTP and the time source.
    /// </remarks>
    public class Program
    {

        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in an NTS server's words, and
        /// the time server's own.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:               "NTSServerCLI",
            Kind:                  NTSServerNode.NTSServerKind,
            DefaultPort:           NTSServerNode.DefaultHTTPPort,
            FrontendSources:       "libs/NTSServer/NTSServer/Frontend",
            ConfigurationSays:     "where the time server, its NTS clients, the name servers and the groups of time servers of this " +
                                  $"NTS server live (default: {WWCPConfigFile.DefaultFileName} below the repository root). Without the file " +
                                   "it hands out this machine's clock on the standard ports, and follows nobody; the \"ntsServer\" section " +
                                   "says otherwise - see the README.",
            CertificateKinds:      NTSServerNode.CertificateKinds,
            Synopsis:              [ "[--local | --upstream [<group>]]", "[--ntske-port <n>]", "[--ntp-port <n>]",
                                     "[--listen <address>]", "[--hostname <name>]", "[--no-ntske-ssh]" ],
            AfterTheWebInterface: [
                .. NodeUsage.Wrap("The time server - each of these says for this start what the \"ntsServer\" section of the " +
                                  "configuration file says for every start:",
                                  "",
                                  ""),
                .. NodeUsage.Switch("--local",              "hand out this machine's clock, as the operating system keeps it."),
                .. NodeUsage.Switch("--upstream [<group>]", "hand out the time of a group of upstream NTS servers, asked over NTS-KE " +
                                                            "(TLS 1.3) and NTP+NTS: one of the node's NTS groups, \"legal\" - the PTB's " +
                                                            "four, unless the \"nts\" section names others - by default. This machine's " +
                                                            "clock is measured, never set: what is handed out is corrected, and nothing " +
                                                            "is handed out before the group has given a time."),
                .. NodeUsage.Switch("--ntske-port <n>",     $"the TCP port of NTS-KE over TLS 1.3 (default: {TimeServerSettings.DefaultNTSKEPort})."),
                .. NodeUsage.Switch("--ntp-port <n>",       $"the UDP port of NTP and NTP+NTS (default: {TimeServerSettings.DefaultNTPPort}). " +
                                                            "On Windows the Windows Time service keeps 123 while it runs; on Linux a port " +
                                                            "below 1024 takes CAP_NET_BIND_SERVICE. With another port, give --hostname too, " +
                                                            "so that the key exchange can tell clients where NTP is."),
                .. NodeUsage.Switch("--listen <address>",   "the address NTS-KE and NTP listen on (default: 0.0.0.0, every IPv4 address; " +
                                                            "\"any\" for IPv6 as well). Not --any, which is the web interface's."),
                .. NodeUsage.Switch("--hostname <name>",    "the name clients reach this server by: what the self-signed TLS identity " +
                                                            "of NTS-KE is made out to."),
                .. NodeUsage.Switch("--no-ntske-ssh",       $"do not serve NTS-KE over SSH (the subsystem {NTSKE_SSH.SubsystemName} of " +
                                                            "the SSH server)."),
                ""
            ]

        );

        #endregion

        #region (private static) WhatToDoAbout(Problem) / SwitchOf(Problem)

        /// <summary>
        /// The way past a port of the time server, in the words of whoever
        /// started it; null for the web interface's, which is every node's.
        /// </summary>
        private static String? WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == TimeServerHost.NTPPort
                   ? "Stop whatever has it - on Windows that is usually the Windows Time service, 'net stop w32time' as an " +
                     "administrator - or give NTP another port with --ntp-port <number>."
                   : Problem.Whose == TimeServerHost.NTSKEPort
                         ? "Stop whatever has it - another NTS server, chrony with ntsserverkey - or give NTS-KE another port with " +
                           "--ntske-port <number>."
                         : null;

        private static String? SwitchOf(PortUnavailableException Problem)

            => Problem.Whose == TimeServerHost.NTPPort
                   ? "--ntp-port"
                   : Problem.Whose == TimeServerHost.NTSKEPort
                         ? "--ntske-port"
                         : null;

        #endregion

        #region (private static) BesideTheWebInterface(Node) / OfTheKind(Node)

        /// <summary>
        /// What the time server listens on, beside the web interface.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> BesideTheWebInterface(NTSServerNode Node)
        {

            var settings  = Node.Settings;
            var identity  = Node.Host.Identity;
            var listen    = TimeServerSettings.TextOf(settings.Listen);

            yield return ("NTS-KE (TLS)",  $"tcp://{listen}:{settings.NTSKEPort}, TLS 1.3, ALPN ntske/1\n" +
                                           $"{identity.Subject}, until {identity.NotAfter:yyyy-MM-dd}\n" +
                                           $"SHA-256 {identity.Fingerprint}\n" +
                                           $"{identity.Source}");

            yield return ("NTP",           $"udp://{listen}:{settings.NTPPort}, NTPv4 and NTP+NTS" +
                                           (Node.Host.AdvertisedNTP is not null ? $"\nthe key exchange names {Node.Host.AdvertisedNTP}" : ""));

            yield return ("NTS-KE (SSH)",  Node.NTSKEOverSSH
                                               ? $"{Node.SSHURL}, subsystem {NTSKE_SSH.SubsystemName}\n" +
                                                 $"{Node.SSHClients.Clients.Count} NTS client(s); accounts allowed to run '{NTSServerAccess.KeyExchange}'"
                                               : settings.SSHEnabled
                                                     ? "switched on, but the SSH server is off"
                                                     : "switched off");

        }

        /// <summary>
        /// Where the time comes from.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> OfTheKind(NTSServerNode Node)
        {

            var settings = Node.Settings;

            if (settings.TimeSource == TimeSource.Local)
                yield return ("time source",  $"this machine's clock, {ClockDescription.Local(settings.Local)}");

            else
            {

                var group = Node.NTSGroups.FirstOrDefault(group => group.Id == settings.Upstream.Group);

                yield return ("time source",  $"upstream, NTS group '{settings.Upstream.Group}', every {settings.Upstream.Poll.TotalSeconds:F0} s\n" +
                                              (group is not null
                                                   ? $"{group.Sources.Describe()}, at least {group.Sources.MinServers} of which must answer\n"
                                                   : "a group this node does not have - see the \"nts\" section\n") +
                                              "nothing is answered before they gave a time");

            }

        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches first. What is left is the time server's
            // own, as it was typed.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            TimeSource?  timeSource     = null;
            String?      upstreamGroup  = null;
            IPPort?      ntsKEPort      = null;
            IPPort?      ntpPort        = null;
            IIPAddress?  listen         = null;
            String?      hostname       = null;
            var          noNTSKEOverSSH = false;

            var rest = arguments.Rest;

            for (var i = 0; i < rest.Count; i++)
            {
                switch (rest[i])
                {

                    case "--local":
                        timeSource = TimeSource.Local;
                        break;

                    case "--upstream":
                        timeSource = TimeSource.Upstream;
                        if (NodeArguments.TryTakeValue(rest, ref i, out var group))
                            upstreamGroup = group;
                        break;

                    case "--ntske-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out var parsedNTSKEPort) && parsedNTSKEPort > 0)
                        {
                            ntsKEPort = IPPort.Parse(parsedNTSKEPort);
                            i++;
                        }
                        else
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid port number after --ntske-port!");
                            return 2;
                        }
                        break;

                    case "--ntp-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out var parsedNTPPort) && parsedNTPPort > 0)
                        {
                            ntpPort = IPPort.Parse(parsedNTPPort);
                            i++;
                        }
                        else
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid port number after --ntp-port!");
                            return 2;
                        }
                        break;

                    case "--listen":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var listenText) ||
                            !TimeServerSettings.TryParseListen(listenText, out listen))
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid address after --listen: an IPv4 or IPv6 address, or \"any\"!");
                            return 2;
                        }
                        break;

                    case "--hostname":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out hostname) ||
                            !TimeServerSettings.IsHostname(hostname))
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid host name after --hostname!");
                            return 2;
                        }
                        break;

                    case "--no-ntske-ssh":
                        noNTSKEOverSSH = true;
                        break;

                    default:
                        return NodeArguments.Unknown(rest[i], Usage);

                }
            }

            var root = NodeProgram.RepositoryRoot("NTSServerCLI.slnx");

            #endregion

            #region The NTS server

            NTSServerNode node;

            try
            {
                node = new NTSServerNode(
                           HTTPHostname:      arguments.HTTPHostname,
                           HTTPPort:          arguments.Port,
                           AccountsPath:      arguments.AccountsPathBelow(root),
                           ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                           Frontend:          arguments.Frontend,
                           CertificatesPath:  arguments.CertificatesPath,
                           ConsoleLogLevel:   arguments.ConsoleLogLevel,
                           LogPath:           arguments.LogPathBelow(root),
                           BridgeDebugLog:    !arguments.NoTrace,
                           SSH:               arguments.SSH,
                           Web:               arguments.Web,

                           // The switches of this start, over the "ntsServer"
                           // section: for this start only, never written back.
                           CommandLine:       settings => settings with {
                                                  TimeSource  = timeSource ?? settings.TimeSource,
                                                  Upstream    = upstreamGroup is not null
                                                                    ? settings.Upstream with { Group = upstreamGroup }
                                                                    : settings.Upstream,
                                                  NTSKEPort   = ntsKEPort  ?? settings.NTSKEPort,
                                                  NTPPort     = ntpPort    ?? settings.NTPPort,
                                                  Listen      = listen     ?? settings.Listen,
                                                  Hostname    = hostname   ?? settings.Hostname,
                                                  SSHEnabled  = !noNTSKEOverSSH && settings.SSHEnabled
                                              }
                       );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(NTSServerNode.NTSServerKind, e, arguments.Verbose);
            }

            await using (node)
            {

                if (node.ImportCertificates(arguments, out _) is Int32 notImported)
                    return notImported;

                if (arguments.ListCertificates)
                    node.ListCertificates();

                if (await node.Started(arguments.Verbose, WhatToDoAbout, SwitchOf: SwitchOf) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                foreach (var line in node.Banner(BesideTheInterfaces:  BesideTheWebInterface(node),
                                                 OfTheKind:            OfTheKind(node)))
                    Console.WriteLine(line);

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new NTSServerCLI(node).RunUntilStopped();

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
