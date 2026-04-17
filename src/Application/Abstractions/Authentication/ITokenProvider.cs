namespace Application.Abstractions.Authentication;

public interface ITokenProvider
{
    TokenInfo Create(Guid userId, string email);
}
