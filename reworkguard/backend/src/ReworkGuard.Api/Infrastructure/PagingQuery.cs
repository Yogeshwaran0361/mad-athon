using System.ComponentModel.DataAnnotations;
using ReworkGuard.Application.Common;

namespace ReworkGuard.Api.Infrastructure;

public sealed class PagingQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, PageRequest.MaxPageSize)]
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public PageRequest ToPageRequest() => new(Page, PageSize);
}
