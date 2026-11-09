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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.SSH;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.NTSServer.CommandLine
{

    /// <summary>
    /// The machines that may make NTS key exchanges over SSH: listed, let in
    /// with a key, switched on or off, and taken out.
    /// </summary>
    /// <remarks>
    /// <code>
    /// ntsClients                                  every NTS client, with its keys
    /// ntsClients add &lt;name&gt; &lt;authorized_keys line&gt;  let a client in with a key - a new client, or one more key
    /// ntsClients remove &lt;name&gt; [&lt;fingerprint&gt;]     take a client out, or one of its keys
    /// ntsClients enable|disable &lt;name&gt;           switch a client on or off
    /// ntsClients ssh on|off                       serve NTS-KE over SSH, or not
    /// </code>
    /// Kept in the "ssh" of the "ntsServer" section of the configuration file,
    /// and in effect at once: a client taken out has its session ended.
    /// </remarks>
    /// <param name="CLI">The command line of the NTS server.</param>
    public class NTSClientsCommand(NTSServerCLI CLI) : ACLICommand<NTSServerCLI>(CLI),
                                                       ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public const String CommandName = "ntsClients";

        private static readonly String[] verbs = [ "add", "remove", "enable", "disable", "ssh" ];

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2)
                return verbs.Where (verb => verb.StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase)).
                             Select(verb => SuggestionResponse.ParameterCompleted($"{CommandName} {verb}"));

            if (Arguments.Length == 3 && Arguments[1] is "remove" or "enable" or "disable")
                return cli.NTSServer.SSHClients.Clients.
                           Where (client => client.Name.StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase)).
                           Select(client => SuggestionResponse.ParameterCompleted($"{CommandName} {Arguments[1]} {client.Name}"));

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            var node = cli.NTSServer;

            if (Arguments.Length == 1)
            {

                if (!cli.MayDo(Permission.Read(NTSServerAccess.TimeServer), CommandName, out var refusedRead))
                    return Task.FromResult<String[]>([ refusedRead ]);

                return Task.FromResult(Listed());

            }

            if (!cli.MayDo(Permission.Edit(NTSServerAccess.TimeServer), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var clients = node.Settings.SSHClients.ToList();
            var verb    = Arguments[1].ToLowerInvariant();

            #region ssh on|off

            if (verb == "ssh")
            {

                if (Arguments.Length != 3 || Arguments[2].ToLowerInvariant() is not ("on" or "off"))
                    return Task.FromResult<String[]>([ Help() ]);

                var on = Arguments[2].Equals("on", StringComparison.OrdinalIgnoreCase);

                if (!node.TrySetSSHEnabled(on, out var notSaved))
                    return Task.FromResult<String[]>([ $"Not saved: {notSaved}" ]);

                return Task.FromResult<String[]>([
                           on ? node.SSHEnabled
                                    ? "NTS-KE is served over SSH from now on."
                                    : "NTS-KE over SSH is switched on - but the SSH server of this node is off, so nobody can reach it yet."
                              : "NTS-KE is no longer served over SSH."
                       ]);

            }

            #endregion

            if (Arguments.Length < 3)
                return Task.FromResult<String[]>([ Help() ]);

            var name   = Arguments[2];
            var index  = clients.FindIndex(client => client.Name == name);

            switch (verb)
            {

                #region add <name> <authorized_keys line>

                case "add":
                {

                    if (Arguments.Length < 4)
                        return Task.FromResult<String[]>([ $"Which key? {Help()}" ]);

                    if (!TimeServerSettings.IsClientName(name))
                        return Task.FromResult<String[]>([ $"'{name}' can not name an NTS client: letters, digits and . _ @ + -, not beginning with a dot." ]);

                    if (index < 0 && User_Id.TryParse(name) is User_Id userId && node.ExtAPI.TryGetUser(userId, out _))
                        return Task.FromResult<String[]>([ $"'{name}' is an account of this {node.Kind.Name} already, and a name that is both is let in as neither. Choose another name." ]);

                    var line = String.Join(" ", Arguments[3..]);

                    if (!TimeServerSettings.IsAuthorizedKeyLine(line, out var why))
                        return Task.FromResult<String[]>([ $"Not an authorized_keys line: {why}" ]);

                    if (index < 0)
                        clients.Add(new NTSClientEntry(name, [ line ]));

                    else
                    {

                        var existing = clients[index];

                        if (existing.Keys.Contains(line))
                            return Task.FromResult<String[]>([ $"The NTS client '{name}' has that key already." ]);

                        clients[index] = existing with { Keys = [ .. existing.Keys, line ] };

                    }

                    return Saved(clients, index < 0
                                              ? $"The NTS client '{name}' may make key exchanges over SSH with {FingerprintOf(line)}."
                                              : $"The NTS client '{name}' may sign in with {FingerprintOf(line)} as well.");

                }

                #endregion

                #region remove <name> [<fingerprint>]

                case "remove":
                {

                    if (index < 0)
                        return Task.FromResult<String[]>([ $"There is no NTS client '{name}'." ]);

                    if (Arguments.Length == 3)
                    {
                        clients.RemoveAt(index);
                        return Saved(clients, $"The NTS client '{name}' is taken out.");
                    }

                    var fingerprint = Arguments[3];
                    var existing    = clients[index];
                    var matching    = existing.Keys.Where(key => FingerprintOf(key).StartsWith(fingerprint, StringComparison.Ordinal) ||
                                                                 FingerprintOf(key).StartsWith("SHA256:" + fingerprint, StringComparison.Ordinal)).
                                                    ToArray();

                    if (matching.Length != 1)
                        return Task.FromResult<String[]>([ matching.Length == 0
                                                               ? $"No key of the NTS client '{name}' has the fingerprint '{fingerprint}'."
                                                               : $"'{fingerprint}' is the beginning of {matching.Length} keys of the NTS client '{name}': say more of it." ]);

                    clients[index] = existing with { Keys = [ .. existing.Keys.Where(key => key != matching[0]) ] };

                    return Saved(clients, $"The NTS client '{name}' no longer signs in with {FingerprintOf(matching[0])}.");

                }

                #endregion

                #region enable / disable <name>

                case "enable":
                case "disable":
                {

                    if (index < 0)
                        return Task.FromResult<String[]>([ $"There is no NTS client '{name}'." ]);

                    var enabled = verb == "enable";

                    clients[index] = clients[index] with { Enabled = enabled };

                    return Saved(clients, $"The NTS client '{name}' is switched {(enabled ? "on" : "off")}.");

                }

                #endregion

                default:
                    return Task.FromResult<String[]>([ Help() ]);

            }

        }

        #endregion


        #region (private) Listed()

        private String[] Listed()
        {

            var node     = cli.NTSServer;
            var clients  = node.Settings.SSHClients;
            var lines    = new List<String> {
                               $"NTS-KE over SSH is {(node.NTSKEOverSSH ? $"served at {node.SSHURL}, subsystem {NTSKE_SSH.SubsystemName}" : node.Settings.SSHEnabled ? "switched on, but the SSH server is off" : "switched off")}."
                           };

            if (clients.Count == 0)
                lines.Add($"No NTS client yet: {CommandName} add <name> <authorized_keys line>. Accounts allowed to run '{NTSServerAccess.KeyExchange}' may make key exchanges as well.");

            foreach (var client in clients)
            {

                lines.Add($"{client.Name}{(client.Enabled ? "" : " (switched off)")}: {client.Keys.Count} key(s)");

                foreach (var key in client.Keys)
                    lines.Add($"    {FingerprintOf(key)}");

            }

            return [ .. lines ];

        }

        #endregion

        #region (private) Saved(Clients, Said)

        private Task<String[]> Saved(IEnumerable<NTSClientEntry>  Clients,
                                     String                       Said)
        {

            if (!cli.NTSServer.TrySetSSHClients(Clients, out var notSaved))
                return Task.FromResult<String[]>([ $"Not saved: {notSaved}" ]);

            cli.NTSServer.Log.Notice($"{Said} ({cli.Caller})", "ntsserver", "ssh", "auth");

            return Task.FromResult<String[]>([ Said ]);

        }

        #endregion

        #region (private static) FingerprintOf(Line)

        private static String FingerprintOf(String Line)

            => AuthorizedKeysFile.TryParseLine(Line, out var key) && key is not null
                   ? $"{key.PublicKey.Sha256Fingerprint} ({key.PublicKey.Algorithm})"
                   : "an unreadable key";

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [add <name> <authorized_keys line> | remove <name> [<fingerprint>] | enable|disable <name> | ssh on|off] - " +
                "the machines that may make NTS key exchanges over SSH";

        #endregion

    }

}
