﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Microsoft.ML.AutoML
{
    internal class MultiModelPipeline
    {
        private static readonly StringEntity _nilStringEntity = new StringEntity("Nil");
        private static readonly EstimatorEntity _nilSweepableEntity = new EstimatorEntity(null);
        private readonly Dictionary<string, SweepableEstimator> _estimators;
        private readonly Entity _schema;

        public MultiModelPipeline()
        {
            this._estimators = new Dictionary<string, SweepableEstimator>();
            this._schema = null;
        }

        internal MultiModelPipeline(Dictionary<string, SweepableEstimator> estimators, Entity schema)
        {
            this._estimators = estimators;
            this._schema = schema;
        }

        public Dictionary<string, SweepableEstimator> Estimators { get => this._estimators; }

        internal Entity Schema { get => this._schema; }

        /// <summary>
        /// Get the schema of all single model pipelines in the form of strings.
        /// the pipeline id can be used to create a single model pipeline through <see cref="MultiModelPipeline.BuildSweepableEstimatorPipeline(string)"/>.
        /// </summary>
        internal string[] PipelineIds { get => this.Schema.ToTerms().Select(t => t.ToString()).ToArray(); }

        public MultiModelPipeline Append(params SweepableEstimator[] estimators)
        {
            Entity entity = null;
            foreach (var estimator in estimators)
            {
                if (entity == null)
                {
                    entity = new EstimatorEntity(estimator);
                    continue;
                }

                entity += estimator;
            }

            return this.Append(entity);
        }

        public MultiModelPipeline AppendOrSkip(params SweepableEstimator[] estimators)
        {
            Entity entity = null;
            foreach (var estimator in estimators)
            {
                if (entity == null)
                {
                    entity = new EstimatorEntity(estimator);
                    continue;
                }

                entity += estimator;
            }

            return this.AppendOrSkip(entity);
        }

        public SweepableEstimatorPipeline BuildSweepableEstimatorPipeline(string schema)
        {
            var pipelineNodes = Entity.FromExpression(schema)
                                      .ValueEntities()
                                      .Where(e => e is StringEntity se && se.Value != "Nil")
                                      .Select((se) => this._estimators[((StringEntity)se).Value]);

            return new SweepableEstimatorPipeline(pipelineNodes);
        }

        internal MultiModelPipeline Append(Entity entity)
        {
            return this.AppendEntity(false, entity);
        }

        internal MultiModelPipeline AppendOrSkip(Entity entity)
        {
            return this.AppendEntity(true, entity);
        }

        internal MultiModelPipeline AppendOrSkip(MultiModelPipeline pipeline)
        {
            return this.AppendPipeline(true, pipeline);
        }

        internal MultiModelPipeline Append(MultiModelPipeline pipeline)
        {
            return this.AppendPipeline(false, pipeline);
        }

        private MultiModelPipeline AppendPipeline(bool allowSkip, MultiModelPipeline pipeline)
        {
            var sweepableEntity = this.CreateSweepableEntityFromEntity(pipeline.Schema, pipeline.Estimators);
            return this.AppendEntity(allowSkip, sweepableEntity);
        }

        private MultiModelPipeline AppendEntity(bool allowSkip, Entity entity)
        {
            var estimators = this._estimators.ToDictionary(x => x.Key, x => x.Value);
            var stringEntity = this.VisitAndReplaceSweepableEntityWithStringEntity(entity, ref estimators);
            if (allowSkip)
            {
                stringEntity += _nilStringEntity;
            }

            var schema = this._schema;
            if (schema == null)
            {
                schema = stringEntity;
            }
            else
            {
                schema *= stringEntity;
            }

            return new MultiModelPipeline(estimators, schema);
        }

        private Entity CreateSweepableEntityFromEntity(Entity entity, Dictionary<string, SweepableEstimator> lookupTable)
        {
            if (entity is null)
            {
                return null;
            }

            if (entity is StringEntity stringEntity)
            {
                if (stringEntity == _nilStringEntity)
                {
                    return _nilSweepableEntity;
                }

                return new EstimatorEntity(lookupTable[stringEntity.Value]);
            }
            else if (entity is ConcatenateEntity concatenateEntity)
            {
                return new ConcatenateEntity()
                {
                    Left = this.CreateSweepableEntityFromEntity(concatenateEntity.Left, lookupTable),
                    Right = this.CreateSweepableEntityFromEntity(concatenateEntity.Right, lookupTable),
                };
            }
            else if (entity is OneOfEntity oneOfEntity)
            {
                return new OneOfEntity()
                {
                    Left = this.CreateSweepableEntityFromEntity(oneOfEntity.Left, lookupTable),
                    Right = this.CreateSweepableEntityFromEntity(oneOfEntity.Right, lookupTable),
                };
            }

            throw new ArgumentException();
        }

        private Entity VisitAndReplaceSweepableEntityWithStringEntity(Entity e, ref Dictionary<string, SweepableEstimator> estimators)
        {
            if (e is null)
            {
                return null;
            }

            if (e is EstimatorEntity sweepableEntity0)
            {
                if (sweepableEntity0 == _nilSweepableEntity)
                {
                    return _nilStringEntity;
                }

                var id = this.GetNextId(estimators);
                estimators[id] = (SweepableEstimator)sweepableEntity0.Estimator;
                return new StringEntity(id);
            }

            e.Left = this.VisitAndReplaceSweepableEntityWithStringEntity(e.Left, ref estimators);
            e.Right = this.VisitAndReplaceSweepableEntityWithStringEntity(e.Right, ref estimators);

            return e;
        }

        private string GetNextId(Dictionary<string, SweepableEstimator> estimators)
        {
            var count = estimators.Count();
            return "e" + count.ToString();
        }
    }
}
