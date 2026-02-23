using System.Linq;
using BoardGameList.Models;

namespace BoardGameList.DTO;

public static class MapBoardGameToDto
{
    public static IQueryable<BoardGameListDto> MapBookToDto(this IQueryable<BoardGame> boardgames)
    {
        return boardgames.Select(boardgame => new BoardGameListDto
        {
            Id = boardgame.Id,
            Name = boardgame.Name,
            Year = boardgame.Year,
            MinPlayers = boardgame.MinPlayers,
            MaxPlayers = boardgame.MaxPlayers,
            PlayTime = boardgame.PlayTime,
            OwnedUsers = boardgame.OwnedUsers,
            MinAge = boardgame.MinAge,
        });
    }
}