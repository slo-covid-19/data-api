﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SloCovidServer.DB.Models;
using SloCovidServer.Handlers;
using SloCovidServer.Mappers;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SloCovidServer.Controllers
{
    [Route("api/models")]
    [ApiController]
    public class ModelsController : MetricsController<ModelsController>
    {
        public record PostData
        {
            public DateTimeOffset Date { get; init; }
            public string Scenario { get; init; }
            public string IntervalType { get; init; }
            public int? IntervalWidth { get; init; }
            public IFormFile Data { get; init; }
        }
        readonly DataContext dataContext;
        public ModelsController(ILogger<ModelsController> logger, DataContext dataContext) : base(logger, communicator: default)
        {
            this.dataContext = dataContext;
        }
        [HttpGet]
        public ActionResult<int> Get()
        {
            return dataContext.ModelsModels.Count();
        }

        [HttpPost]
        [Authorize]
        public async Task<ActionResult> Post([FromForm] PostData data)
        {
            if (User.Identity is ModelClaimsIdentity modelIdentity)
            {
                try
                {
                    if (data.Data is null)
                    {
                        return StatusCode(StatusCodes.Status406NotAcceptable, "Missing data file");
                    }
                    var prediction = await GetModelPredictionAsync(modelIdentity.ModelId, data);
                    var modelsMapper = new ModelsMapper();
                    ImmutableArray<ModelsPredictiondatum> items;
                    using (var dataStream = data.Data.OpenReadStream())
                    {
                        items = await modelsMapper.MapFromRawAsync(dataStream);
                        foreach (var item in items)
                        {
                            prediction.Model.ModelsPredictiondata.Add(item);
                        }
                    }
                    using (var transaction = await dataContext.Database.BeginTransactionAsync())
                    {
                        if (!prediction.IsNew)
                        {
                            // delete old data first
                            int result = await dataContext.Database.ExecuteSqlRawAsync("DELETE FROM models_predictiondata WHERE prediction_id={0}", prediction.Model.Id);
                            logger.LogInformation($"Deleted {result} models_predictiondata child items for prediction update");
                        }
                        else
                        {
                            dataContext.ModelsPredictions.Add(prediction.Model);
                        }
                        await dataContext.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    return Ok();
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status400BadRequest, ex.Message);
                }
            }
            else
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        async Task<(ModelsPrediction Model, bool IsNew)> GetModelPredictionAsync(Guid modelId, PostData data, CancellationToken ct = default)
        {
            ModelsPredictionintervaltype intervalType = null;
            if (!string.IsNullOrEmpty(data.IntervalType))
            {
                intervalType = await dataContext.ModelsPredictionintervaltypes.Where(it => it.Name == data.IntervalType).SingleOrDefaultAsync()
                    ?? throw new Exception($"Invalid interval type {data.IntervalType}");
            }
            ModelsPredictionintervalwidth intervalWidth = null;
            if (data.IntervalWidth is not null)
            {
                intervalWidth = await dataContext.ModelsPredictionintervalwidths.Where(iw => iw.Width == data.IntervalWidth).SingleOrDefaultAsync()
                    ?? throw new Exception($"Invalid interval width {data.IntervalWidth}");
            }
            var scenario = await dataContext.ModelsScenarios.Where(m => m.Name == data.Scenario).SingleOrDefaultAsync(ct)
                    ?? throw new Exception($"Invalid scenario {data.Scenario}");
            var model = await dataContext.ModelsPredictions.Where(m =>
                m.ModelId == modelId
                && m.Date == data.Date && m.Scenario == scenario && m.IntervalType == intervalType)
                .SingleOrDefaultAsync(ct);
            bool isNew = model is null;
            if (isNew)
            {
                model = new ModelsPrediction
                {
                    ModelId = modelId,
                    Date = data.Date.Date,
                    Scenario = scenario,
                    IntervalType = intervalType,
                    Created = DateTime.Now,
                };
            }
            model.Updated = DateTime.Now;
            model.IntervalWidth = intervalWidth;
            return (model, isNew);
        }
    }
}
