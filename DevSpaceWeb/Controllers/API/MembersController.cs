﻿using DevSpaceWeb.API;
using DevSpaceWeb.API.Teams;
using DevSpaceWeb.Data;
using DevSpaceWeb.Data.Permissions;
using DevSpaceWeb.Data.Teams;
using DevSpaceWeb.Data.Users;
using DevSpaceWeb.Database;
using DevSpaceWeb.Extensions;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Swashbuckle.AspNetCore.Annotations;
using System.Data;

namespace DevSpaceWeb.Controllers.API;

[ShowInSwagger]
[SwaggerTag("Manage Team Members and information.")]
[IsAuthenticated]
[SwaggerResponse(StatusCodes.Status401Unauthorized, "Unauthorized", typeof(ResponseUnauthorized))]
[SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden", typeof(ResponseForbidden))]
[SwaggerResponse(StatusCodes.Status400BadRequest, "Bad Request", typeof(ResponseBadRequest))]
public class MembersController : APIController
{
    [HttpGet("/api/members")]
    [SwaggerOperation("Get a list of members.", "Requires View Members permission.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<MemberJson[]>))]
    public async Task<IActionResult> GetMembers()
    {
        if (Client.CheckFailedTeamPermissions(TeamPermission.ViewMembers, out TeamPermission? perm))
            return PermissionFailed(perm!);

        bool ViewPerms = Client.HasTeamPermission(CurrentTeam, TeamPermission.ViewPermissions);

        return Ok(CurrentTeam.CachedMembers.Values.Select(x => new MemberJson(x, ViewPerms)));
    }

    [HttpGet("/api/members/{userId?}")]
    [SwaggerOperation("Get a member.", "Requires View Members permission.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseData<MemberJson>))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> GetMember([FromRoute] string userId = "")
    {
        if (Client.CheckFailedTeamPermissions(TeamPermission.ViewMembers, out TeamPermission? perm))
            return PermissionFailed(perm!);

        if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out ObjectId obj2) || !CurrentTeam.Members.TryGetValue(obj2, out ObjectId memberObj) || !CurrentTeam.CachedMembers.TryGetValue(memberObj, out TeamMemberData? member))
            return NotFound("Could not find member.");

        return Ok(new MemberJson(member, Client.HasTeamPermission(CurrentTeam, TeamPermission.ViewPermissions)));
    }

    [HttpPatch("/api/members/{userId?}/enable")]
    [SwaggerOperation("Enable a member.", "Requires View Members and Manage Members permissions.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden", typeof(ResponseForbidden))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> EnableMember([FromRoute] string userId = "")
    {
        if (Client.CheckFailedTeamPermissions(TeamPermission.ViewMembers | TeamPermission.ManageMembers, out TeamPermission? perm))
            return PermissionFailed(perm!);

        if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out ObjectId obj2) || !CurrentTeam.Members.TryGetValue(obj2, out ObjectId memberObj) || !CurrentTeam.CachedMembers.TryGetValue(memberObj, out TeamMemberData? member))
            return NotFound("Could not find member.");

        if (member.GetRank() >= Client.GetRank())
            return RankFailed();

        PartialUserData user = _DB.Users[member.UserId];

        if (user.ManagedAccountTeamId.HasValue)
        {
            FilterDefinition<AuthUser> filter = Builders<AuthUser>.Filter.Eq(r => r.Id, user.Id);
            UpdateDefinition<AuthUser> update = Builders<AuthUser>.Update.Set(x => x.Disabled, null);
            UpdateResult Result = _DB.Client.GetDatabase(_DB.Configure!.Name).GetCollection<AuthUser>("users").UpdateOne(filter, update);
            if (!Result.IsAcknowledged)
                return InternalServerError("Failed to disable member.");
        }

        await member.UpdateAsync(Builders<TeamMemberData>.Update.Set(x => x.Disabled, null), () =>
        {
            _DB.TriggerSessionEvent(user.Id, SessionEventType.AccountUpdate);
            member.Disabled = null;
        });

        return Ok();
    }

    [HttpPatch("/api/members/{userId?}/disable")]
    [SwaggerOperation("Disable a member.", "Requires View Members and Manage Members permissions.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden", typeof(ResponseForbidden))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> DisableMember([FromRoute] string userId = "")
    {
        if (Client.CheckFailedTeamPermissions(TeamPermission.ViewMembers | TeamPermission.ManageMembers, out TeamPermission? perm))
            return PermissionFailed(perm!);

        if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out ObjectId obj2) || !CurrentTeam.Members.TryGetValue(obj2, out ObjectId memberObj) || !CurrentTeam.CachedMembers.TryGetValue(memberObj, out TeamMemberData? member))
            return NotFound("Could not find member.");



        if (member.GetRank() >= Client.GetRank())
            return RankFailed();

        UserDisabled Disabled = new UserDisabled
        {
            DisabledAt = DateTime.UtcNow,
            State = UserDisabledState.TeamManaged
        };

        PartialUserData user = _DB.Users[member.UserId];

        if (user.ManagedAccountTeamId.HasValue)
        {
            FilterDefinition<AuthUser> filter = Builders<AuthUser>.Filter.Eq(r => r.Id, user.Id);
            UpdateDefinition<AuthUser> update = Builders<AuthUser>.Update.Set(x => x.Disabled, Disabled);
            UpdateResult Result = _DB.Client.GetDatabase(_DB.Configure!.Name).GetCollection<AuthUser>("users").UpdateOne(filter, update);
            if (!Result.IsAcknowledged)
                return InternalServerError("Failed to disable member.");
        }

        await member.UpdateAsync(Builders<TeamMemberData>.Update.Set(x => x.Disabled, Disabled), () =>
        {
            _DB.TriggerSessionEvent(user.Id, SessionEventType.AccountUpdate);
            member.Disabled = Disabled;
        });

        return Ok();
    }

    [HttpDelete("/api/members/{userId?}/remove")]
    [SwaggerOperation("Remove a member.", "Requires View Members and Manage Members permissions.")]
    [SwaggerResponse(StatusCodes.Status200OK, "Success", typeof(ResponseSuccess))]
    [SwaggerResponse(StatusCodes.Status403Forbidden, "Forbidden", typeof(ResponseForbidden))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "Not Found", typeof(ResponseNotFound))]
    public async Task<IActionResult> RemoveMember([FromRoute] string userId = "")
    {
        if (Client.CheckFailedTeamPermissions(TeamPermission.ViewMembers | TeamPermission.ManageMembers, out TeamPermission? perm))
            return PermissionFailed(perm!);

        if (string.IsNullOrEmpty(userId) || !ObjectId.TryParse(userId, out ObjectId obj2) || !CurrentTeam.Members.TryGetValue(obj2, out ObjectId memberObj) || !CurrentTeam.CachedMembers.TryGetValue(memberObj, out TeamMemberData? member))
            return NotFound("Could not find member.");

        if (member.GetRank() >= Client.GetRank())
            return RankFailed();

        await CurrentTeam.UpdateAsync(new UpdateDefinitionBuilder<TeamData>().Set(x => x.Members, CurrentTeam.Members), () =>
        {
            _DB.Members.Collection.DeleteOneAsync(new FilterDefinitionBuilder<TeamMemberData>().Eq(x => x.Id, member.Id));
            CurrentTeam.Members.Remove(member.UserId);
            CurrentTeam.CachedMembers.Remove(member.Id);
            _DB.Members.Cache.Remove(member.Id, out _);
            //_ = _DB.AuditLogs.CreateAsync(new AuditLog(Client.geto, AuditLogCategoryType.Member, AuditLogEventType.MemberRemoved)
            //.SetTarget(_DB.Users[user])
            //.AddProperty("User", _DB.Users[user].UserName)
            //.AddProperty("ID", _DB.Users[user].Id.ToString()));
        });


        return Ok();
    }
}
