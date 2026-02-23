using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text.Json;
using System.Threading.Tasks;
using BoardGameList.Constants;
using BoardGameList.DTO;
using BoardGameList.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace BoardGameList.Controllers;

[Route("[controller]")]
[ApiController]
public class BoardGamesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    private readonly ILogger<BoardGamesController> _logger;
    
    private readonly IMemoryCache _memoryCache;

    public BoardGamesController(ApplicationDbContext context, 
        ILogger<BoardGamesController> logger,
        IMemoryCache memoryCache)
    {
        _context = context;
        _logger = logger;
        _memoryCache = memoryCache;
    }

    [HttpGet(Name = "GetBoardGames")]
    [ResponseCache(CacheProfileName = "Any-60")]
    [SwaggerOperation(Summary = "Get a list of board games.",
        Description = "Retrieves a list of board games with custom paging, sorting, and filtering rules.")]
    public async Task<RestDTO<BoardGameListDto[]>> Get(
        [FromQuery]
        [SwaggerParameter("A DTO object that can be used to customize some retrieval parameters.")]
        RequestDTO<BoardGameDTO> input)
    {
        _logger.LogInformation(CustomLogEvents.BoardGamesController_Get, "Get Board Game List method started");
        var query = _context.BoardGames.MapBookToDto().AsQueryable();
        if (!string.IsNullOrEmpty(input.FilterQuery))
            query = query.Where(b => b.Name.Contains(input.FilterQuery));
        var recordCount = await query.CountAsync();
        
        BoardGameListDto[]? result = null;
        var cacheKey = $"{input.GetType()}-{JsonSerializer.Serialize(input)}";
        if (!_memoryCache.TryGetValue<BoardGameListDto[]>(cacheKey, out result))
        {
            query = query
                .OrderBy($"{input.SortColumn} {input.SortOrder}")
                .Skip(input.PageIndex * input.PageSize)
                .Take(input.PageSize);
            result = await query.ToArrayAsync();
            _memoryCache.Set(cacheKey, result, new TimeSpan(0, 0, 30));
        }

        return new RestDTO<BoardGameListDto[]>()
        {
            Data = result,
            PageIndex = input.PageIndex,
            PageSize = input.PageSize,
            RecordCount = recordCount,
            Links = new List<LinkDTO> {
                new LinkDTO(Url.Action(null, "BoardGames", new { input.PageIndex, input.PageSize }, Request.Scheme)!,
                    "self", 
                    "GET"),
            }
        };
    }
    
    [Authorize(Roles = RoleNames.Moderator)]
    [HttpPost(Name = "UpdateBoardGame")]
    [ResponseCache(CacheProfileName = "NoCache")]
    [SwaggerOperation(
        Summary = "Updates a board game.",
        Description = "Updates the board game's data.")]
    public async Task<RestDTO<BoardGame?>> Post(BoardGameDTO model)
    {
        var boardgame = await _context.BoardGames
            .Where(b => b.Id == model.Id)
            .FirstOrDefaultAsync();
        if (boardgame != null)
        {
            if (!string.IsNullOrEmpty(model.Name))
                boardgame.Name = model.Name;
            if (model.Year.HasValue && model.Year.Value > 0)
                boardgame.Year = model.Year.Value;
            if (model.MinPlayers.HasValue && model.MinPlayers.Value > 0)
                boardgame.MinPlayers = model.MinPlayers.Value;
            if (model.MaxPlayers.HasValue && model.MaxPlayers.Value > 0)
                boardgame.MaxPlayers = model.MaxPlayers.Value;
            if (model.PlayTime.HasValue && model.PlayTime.Value > 0)
                boardgame.PlayTime = model.PlayTime.Value;
            if (model.MinAge.HasValue && model.MinAge.Value > 0)
                boardgame.MinAge = model.MinAge.Value;
            boardgame.LastModifiedDate = DateTime.Now;
            _context.BoardGames.Update(boardgame);
            await _context.SaveChangesAsync();
        };

        return new RestDTO<BoardGame?>()
        {
            Data = boardgame,
            Links = new List<LinkDTO>
            {
                new LinkDTO(
                    Url.Action(null, "BoardGames", model, Request.Scheme)!,
                    "self",
                    "POST"),
            }
        };
    }
    
    [Authorize(Roles = RoleNames.Administrator)]
    [HttpDelete(Name = "DeleteBoardGame")]
    [ResponseCache(CacheProfileName = "NoCache")]
    [SwaggerOperation(
        Summary = "Deletes a board game.",
        Description = "Deletes a board game from the database.")]
    public async Task<RestDTO<BoardGame?>> Delete(int id)
    {
        var boardgame = await _context.BoardGames
            .Where(b => b.Id == id)
            .FirstOrDefaultAsync();
        if (boardgame != null)
        {
            _context.BoardGames.Remove(boardgame);
            await _context.SaveChangesAsync();
        };

        return new RestDTO<BoardGame?>()
        {
            Data = boardgame,
            Links = new List<LinkDTO>
            {
                new LinkDTO(
                    Url.Action(null, "BoardGames", id, Request.Scheme)!,
                    "self",
                    "DELETE"),
            }
        };
    }
}