using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Web.Blazor.Authentication.TokenStore;
using Web.Blazor.Gateway.Contracts;

namespace Web.Blazor.Gateway;

internal sealed class AdminGatewayClient(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ITokenStore tokenStore) : IAdminGatewayClient
{
    private const string AdminBase = "/api/auth/admin";

    // ----- Users -----
    public async Task<PagedResponse<UserSummary>> ListUsersAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        string query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }

        return await GetAsync<PagedResponse<UserSummary>>($"{AdminBase}/users{query}", ct);
    }

    public Task<UserDetailResponse?> GetUserAsync(Guid id, CancellationToken ct)
        => GetOrNullAsync<UserDetailResponse>($"{AdminBase}/users/{id}", ct);

    public async Task<Guid> PreProvisionUserAsync(string email, string displayName, string? netSuiteEmail, CancellationToken ct)
    {
        var body = new { email, displayName, netSuiteEmail };
        var result = await PostAsync<object, IdResponse>($"{AdminBase}/users/pre-provision", body, ct);
        return result.Id;
    }

    public Task DisableUserAsync(Guid id, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/users/{id}/disable", content: null, ct);

    public Task EnableUserAsync(Guid id, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/users/{id}/enable", content: null, ct);

    public Task SetNetSuiteEmailAsync(Guid id, string? netSuiteEmail, CancellationToken ct)
        => SendJsonAsync(HttpMethod.Put, $"{AdminBase}/users/{id}/netsuite-email", new { netSuiteEmail }, ct);

    public Task AssignRoleToUserAsync(Guid userId, Guid roleId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/users/{userId}/roles/{roleId}", content: null, ct);

    public Task RevokeRoleFromUserAsync(Guid userId, Guid roleId, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, $"{AdminBase}/users/{userId}/roles/{roleId}", content: null, ct);

    public Task AddUserToGroupAsync(Guid userId, Guid groupId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/users/{userId}/groups/{groupId}", content: null, ct);

    public Task RemoveUserFromGroupAsync(Guid userId, Guid groupId, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, $"{AdminBase}/users/{userId}/groups/{groupId}", content: null, ct);

    // ----- Groups -----
    public async Task<PagedResponse<GroupSummary>> ListGroupsAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        string query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }

        return await GetAsync<PagedResponse<GroupSummary>>($"{AdminBase}/groups{query}", ct);
    }

    public Task<GroupDetailResponse?> GetGroupAsync(Guid id, CancellationToken ct)
        => GetOrNullAsync<GroupDetailResponse>($"{AdminBase}/groups/{id}", ct);

    public async Task<Guid> CreateGroupAsync(string name, string description, Guid? entraGroupId, CancellationToken ct)
    {
        var body = new { name, description, entraGroupId };
        var result = await PostAsync<object, IdResponse>($"{AdminBase}/groups", body, ct);
        return result.Id;
    }

    public Task UpdateGroupAsync(Guid id, string name, string description, Guid? entraGroupId, CancellationToken ct)
        => SendJsonAsync(HttpMethod.Put, $"{AdminBase}/groups/{id}", new { name, description, entraGroupId }, ct);

    public Task DeleteGroupAsync(Guid id, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, $"{AdminBase}/groups/{id}", content: null, ct);

    public Task AssignRoleToGroupAsync(Guid groupId, Guid roleId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/groups/{groupId}/roles/{roleId}", content: null, ct);

    public Task RevokeRoleFromGroupAsync(Guid groupId, Guid roleId, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, $"{AdminBase}/groups/{groupId}/roles/{roleId}", content: null, ct);

    // ----- Roles -----
    public async Task<PagedResponse<RoleSummary>> ListRolesAsync(int page, int pageSize, string? search, CancellationToken ct)
    {
        string query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }

        return await GetAsync<PagedResponse<RoleSummary>>($"{AdminBase}/roles{query}", ct);
    }

    public Task<RoleDetailResponse?> GetRoleAsync(Guid id, CancellationToken ct)
        => GetOrNullAsync<RoleDetailResponse>($"{AdminBase}/roles/{id}", ct);

    public async Task<Guid> CreateRoleAsync(string name, string description, CancellationToken ct)
    {
        var body = new { name, description };
        var result = await PostAsync<object, IdResponse>($"{AdminBase}/roles", body, ct);
        return result.Id;
    }

    public Task UpdateRoleAsync(Guid id, string name, string description, CancellationToken ct)
        => SendJsonAsync(HttpMethod.Put, $"{AdminBase}/roles/{id}", new { name, description }, ct);

    public Task DeleteRoleAsync(Guid id, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, $"{AdminBase}/roles/{id}", content: null, ct);

    public Task AssignPermissionToRoleAsync(Guid roleId, Guid permissionId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/roles/{roleId}/permissions/{permissionId}", content: null, ct);

    public Task RevokePermissionFromRoleAsync(Guid roleId, Guid permissionId, CancellationToken ct)
        => SendAsync(HttpMethod.Delete, $"{AdminBase}/roles/{roleId}/permissions/{permissionId}", content: null, ct);

    // ----- Permissions -----
    public Task<IReadOnlyCollection<PermissionResponse>> ListPermissionsAsync(CancellationToken ct)
        => GetAsync<IReadOnlyCollection<PermissionResponse>>($"{AdminBase}/permissions", ct);

    // ----- M2M Clients -----
    public Task<PagedResponse<M2MClientSummary>> ListM2MClientsAsync(int page, int pageSize, CancellationToken ct)
        => GetAsync<PagedResponse<M2MClientSummary>>($"{AdminBase}/m2m-clients?page={page}&pageSize={pageSize}", ct);

    public Task<M2MClientDetailResponse?> GetM2MClientAsync(Guid id, CancellationToken ct)
        => GetOrNullAsync<M2MClientDetailResponse>($"{AdminBase}/m2m-clients/{id}", ct);

    public Task<CreateM2MClientResponse> CreateM2MClientAsync(
        string clientId,
        string displayName,
        IReadOnlyCollection<string> allowedScopes,
        CancellationToken ct)
        => PostAsync<object, CreateM2MClientResponse>(
            $"{AdminBase}/m2m-clients",
            new { clientId, displayName, allowedScopes },
            ct);

    public async Task<string> RegenerateM2MClientSecretAsync(Guid id, CancellationToken ct)
    {
        var result = await PostAsync<object?, SecretResponse>($"{AdminBase}/m2m-clients/{id}/regenerate-secret", null, ct);
        return result.ClientSecret;
    }

    public Task DeactivateM2MClientAsync(Guid id, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"{AdminBase}/m2m-clients/{id}/deactivate", content: null, ct);

    // ----- Audit -----
    public Task<PagedResponse<AuthAuditEventResponse>> ListAuditEventsAsync(
        int page,
        int pageSize,
        Guid? userId,
        string? eventType,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        string query = $"?page={page}&pageSize={pageSize}";
        if (userId.HasValue)
        {
            query += $"&userId={userId.Value}";
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query += $"&eventType={Uri.EscapeDataString(eventType)}";
        }
        if (from.HasValue)
        {
            query += $"&from={Uri.EscapeDataString(from.Value.ToString("o", CultureInfo.InvariantCulture))}";
        }
        if (to.HasValue)
        {
            query += $"&to={Uri.EscapeDataString(to.Value.ToString("o", CultureInfo.InvariantCulture))}";
        }

        return GetAsync<PagedResponse<AuthAuditEventResponse>>($"{AdminBase}/audit-events{query}", ct);
    }

    // ----- Internals -----
    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? sessionId = httpContextAccessor.HttpContext?.User.FindFirstValue("session_id")
            ?? throw new UnauthorizedAccessException("No session id on principal.");

        SessionTokens tokens = await tokenStore.GetAsync(sessionId, ct)
            ?? throw new UnauthorizedAccessException("No tokens for session.");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        await AuthorizeAsync(req, ct);
        using HttpResponseMessage resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(ct)
            ?? throw new InvalidOperationException("Empty response from gateway.");
    }

    private async Task<T?> GetOrNullAsync<T>(string path, CancellationToken ct)
        where T : class
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        await AuthorizeAsync(req, ct);
        using HttpResponseMessage resp = await httpClient.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(ct);
    }

    private async Task<TResp> PostAsync<TBody, TResp>(string path, TBody body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        await AuthorizeAsync(req, ct);
        using HttpResponseMessage resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TResp>(ct)
            ?? throw new InvalidOperationException("Empty response from gateway.");
    }

    private async Task SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path) { Content = content };
        await AuthorizeAsync(req, ct);
        using HttpResponseMessage resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task SendJsonAsync<TBody>(HttpMethod method, string path, TBody body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        await AuthorizeAsync(req, ct);
        using HttpResponseMessage resp = await httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record IdResponse(Guid Id);

    private sealed record SecretResponse(string ClientSecret);
}
