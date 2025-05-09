﻿using DaRT;
using DevSpaceWeb.API;
using DevSpaceWeb.API.Consoles;
using DevSpaceWeb.Data;
using DevSpaceWeb.Data.Permissions;
using DevSpaceWeb.Database;
using DevSpaceWeb.Extensions;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace DevSpaceWeb.Controllers.API;

[ShowInSwagger]
[SwaggerTag("Requires permission View Consoles")]
[IsAuthenticated]
[SwaggerResponse(StatusCodes.Status401Unauthorized, "Unauthorized", typeof(ResponseUnauthorized))]
[SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden", typeof(ResponseForbidden))]
[SwaggerResponse(StatusCodes.Status400BadRequest, "Bad Request", typeof(ResponseBadRequest))]
public class ConsoleController : APIController
{
    [HttpGet("/api/consoles")]
    [SwaggerOperation("Get a list of consoles.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<ConsoleJson[]>))]
    public async Task<IActionResult> GetConsoles([FromQuery] bool showIp = false)
    {
        return Ok(_DB.Consoles.Cache.Values.Where(x => (x.TeamId == Client.TeamId) && Client.HasConsolePermission(CurrentTeam, x, ConsolePermission.ViewConsole)).Select(x => new ConsoleJson(x, showIp && Client.HasConsolePermission(CurrentTeam, x, ConsolePermission.ConsoleAdministrator))));
    }

    [HttpGet("/api/consoles/{consoleId?}")]
    [SwaggerOperation("Get a console.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<ConsoleJson>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> GetConsole([FromRoute] string consoleId = "", [FromQuery] bool showIp = false)
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (showIp)
        {
            if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ConsoleAdministrator, out perm))
                return PermissionFailed(perm);
        }

        return Ok(new ConsoleJson(server, showIp));
    }

    [HttpGet("/api/consoles/{consoleId?}/server/players")]
    [SwaggerOperation("Get a list of player names on the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<string[]>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> GetPlayers([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ViewPlayers, out ConsolePermission? perm))
            return PermissionFailed(perm);

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    List<Player> Players = rcon.GetPlayers();
                    return Ok(Players.Select(x => x.name));
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    string ListCommand = await rcon.ExecuteCmd("list");
                    try
                    {
                        ListCommand = ListCommand.Split("online:").Last();
                    }
                    catch { }

                    List<Player> list = new List<Player>();

                    foreach (Player? i in ListCommand.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new Player(0, null, null, null, x, null)))
                    {
                        if (i.name.Contains(" and "))
                        {
                            string[] SplitAnd = i.name.Split(" and ");
                            list.Add(new Player(0, null, null, null, SplitAnd[0], null));
                            list.Add(new Player(0, null, null, null, SplitAnd[1], null));
                        }
                        else
                            list.Add(i);
                    }

                    return Ok(list.Select(x => x.name));
                }
        }

        return Conflict("Failed to get players list");
    }

    [HttpGet("/api/consoles/{consoleId?}/battleye/players")]
    [SwaggerOperation("Get a list of battleye players on the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<ConsolePlayerJson[]>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> GetPlayersBattleye([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ViewPlayers, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        bool ShowIp = Client.HasConsolePermission(server.Team, server, ConsolePermission.ViewIPs);

        List<Player> Players = rcon.GetPlayers();
        return Ok(Players.Select(x => new ConsolePlayerJson(x, ShowIp)));
    }

    [HttpPost("/api/consoles/{consoleId?}/server/command")]
    [SwaggerOperation("Execute a command on the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<string>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> ExecuteCommand([FromRoute] string consoleId = "", [FromBody, Required] ConsoleCommandJson? command = null)
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.UseConsoleCommands, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (command == null || string.IsNullOrEmpty(command.command))
            return BadRequest("Command argument is required.");

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    rcon.ExecuteCommand(command.command);
                    return Ok("");
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    string Command = await rcon.ExecuteCmd(command.command);
                    if (Command.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Unknown command");

                    return Ok(Command);
                }
        }

        return Conflict("Failed to send command");
    }

    [HttpPost("/api/consoles/{consoleId?}/message/global")]
    [SwaggerOperation("Send a global message.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> MessageGlobal([FromRoute] string consoleId = "", [FromBody, Required] ConsoleMessageJson? message = null)
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.MessageGlobal, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (message == null || string.IsNullOrEmpty(message.message))
            return BadRequest("Message argument is required.");

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    rcon.SayGlobal(message.message);
                    return Ok();
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    await rcon.ExecuteCmd("say " + message);
                    return Ok();
                }
        }

        return Conflict("Failed to global message");
    }

    [HttpPost("/api/consoles/{consoleId?}/message/player")]
    [SwaggerOperation("Send a message to a player.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> MessagePlayer([FromRoute] string consoleId = "", [FromBody, Required] ConsoleMessagePlayerJson? json = null)
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.MessagePlayers, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (json == null || string.IsNullOrEmpty(json.player))
            return BadRequest("Player argument is required.");

        if (string.IsNullOrEmpty(json.message))
            return BadRequest("Message argument is required.");

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    if (!int.TryParse(json.player, out int number))
                        return BadRequest("Invalid player number.");

                    rcon.SayPrivate(new Message(number, "admin", json.message));
                    return Ok();
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    await rcon.ExecuteCmd($"tell {json.player} {json.message}");
                    return Ok();
                }
        }

        return Conflict("Failed to message player");
    }

    [HttpPut("/api/consoles/{consoleId?}/players/kick")]
    [SwaggerOperation("Kick a player on the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> KickPlayer([FromRoute] string consoleId = "", [FromBody, Required] ConsoleReasonPlayerJson? json = null)
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.KickPlayers, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (json == null || string.IsNullOrEmpty(json.player))
            return BadRequest("Player argument is required.");

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    if (!int.TryParse(json.player, out int number))
                        return BadRequest("Invalid player number.");

                    rcon.KickPlayer(new Kick(number, "admin", json.reason));
                    return Ok();
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    await rcon.ExecuteCmd($"kick {json.player} {json.reason}");


                    return Ok();
                }
        }

        return Conflict("Failed to kick player");
    }

    [HttpPut("/api/consoles/{consoleId?}/players/ban")]
    [SwaggerOperation("Ban a player on the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> BanPlayer([FromRoute] string consoleId = "", [FromBody, Required] ConsoleReasonPlayerJson? json = null)
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.BanPlayers, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (json == null || string.IsNullOrEmpty(json.player))
            return BadRequest("Player argument is required.");

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    if (!int.TryParse(json.player, out int number))
                        return BadRequest("Invalid player number.");

                    rcon.BanPlayer(new Ban(number, "admin", "", "", true));
                    return Ok();
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    await rcon.ExecuteCmd($"ban {json.player} {json.reason}");


                    return Ok();
                }
        }

        return Conflict("Failed to ban player");
    }

    [HttpPost("/api/consoles/{consoleId?}/server/restart")]
    [SwaggerOperation("Restart the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> RestartServer([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    rcon.RestartServer();
                    return Ok();
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    string Result = await rcon.ExecuteCmd("restart");
                    if (Result.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Server does not support restart.");

                    return Ok();
                }
        }

        return Conflict("Failed to restart server");
    }

    [HttpPost("/api/consoles/{consoleId?}/server/shutdown")]
    [SwaggerOperation("Shutdown the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> ShutdownServer([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);


        switch (server.Type)
        {
            case Data.Consoles.ConsoleType.Battleye:
                {
                    if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    rcon.ShutdownServer();
                    return Ok();
                }
            case Data.Consoles.ConsoleType.Minecraft:
                {
                    if (!_Data.MinecraftRcons.TryGetValue(server.Id, out LibMCRcon.RCon.TCPRconAsync? rcon) || !rcon.IsConnected)
                        return Conflict("Rcon connection is unavailable or server is offline.");

                    string Result = await rcon.ExecuteCmd("stop");
                    if (Result.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("Server does not support shutdown.");

                    return Ok();
                }
        }

        return Conflict("Failed to shutdown server");
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/lock")]
    [SwaggerOperation("Lock the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> LockServer([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.LockServer();
        return Ok();
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/unlock")]
    [SwaggerOperation("Unlock the server.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> UnlockServer([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.UnlockServer();
        return Ok();
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/reload/config")]
    [SwaggerOperation("Reload the config.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> ReloadConfig([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.ReloadConfig();
        return Ok();
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/reload/events")]
    [SwaggerOperation("Reload the events.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> ReloadEvents([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.ReloadEvents();
        return Ok();
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/reload/scripts")]
    [SwaggerOperation("Reload the scripts.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> ReloadScripts([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.ReloadScripts();
        return Ok();
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/reload/bans")]
    [SwaggerOperation("Reload the bans.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> ReloadBans([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.ReloadBans();
        return Ok();
    }

    [HttpPost("/api/consoles/{consoleId?}/battleye/reassign")]
    [SwaggerOperation("Reload the config.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> Reassign([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ControlServer, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        rcon.Reassign();
        return Ok();
    }

    [HttpGet("/api/consoles/{consoleId?}/battleye/connection")]
    [SwaggerOperation("Get a list of rcon connections.", "")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<ConsoleAdminJson[]>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> Admins([FromRoute] string consoleId = "")
    {
        if (string.IsNullOrEmpty(consoleId) || !ObjectId.TryParse(consoleId, out ObjectId obj) || !_DB.Consoles.Cache.TryGetValue(obj, out Data.Consoles.ConsoleData? server) || !(server.TeamId == Client.TeamId))
            return NotFound("Could not find console.");

        if (Client.CheckFailedConsolePermissions(server, ConsolePermission.ViewConsole | ConsolePermission.ViewConnections, out ConsolePermission? perm))
            return PermissionFailed(perm);

        if (server.Type != Data.Consoles.ConsoleType.Battleye)
            return BadRequest("Invalid console type that can't be used for this endpoint.");

        if (!_Data.BattleyeRcons.TryGetValue(server.Id, out RCon? rcon) || !rcon.IsConnected)
            return Conflict("Rcon connection is unavailable or server is offline.");

        bool ShowIp = Client.HasConsolePermission(server.Team, server, ConsolePermission.ViewIPs);

        List<RconAdmin>? Admins = rcon.GetAdmins();
        return Ok(Admins.Select(x => new ConsoleAdminJson(x, ShowIp)));
    }
}
