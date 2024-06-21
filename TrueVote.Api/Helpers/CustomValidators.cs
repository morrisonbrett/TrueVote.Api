#pragma warning disable IDE0046 // Convert to conditional expression
using Microsoft.EntityFrameworkCore;
using Namotion.Reflection;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using TrueVote.Api.Interfaces;
using TrueVote.Api.Models;
using TrueVote.Api.Services;

namespace TrueVote.Api.Helpers
{
    public static class RecursiveValidator
    {
        public static bool TryValidateObjectRecursive(object obj, ValidationContext validationContext, List<ValidationResult> results)
        {
            if (obj == null) return true;
            var result = Validator.TryValidateObject(obj, validationContext, results, true);

            foreach (var property in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = property.GetValue(obj);
                if (value == null) continue;

                // Check if the property is a collection
                if (value is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item != null)
                        {
                            var nestedContext = new ValidationContext(item, validationContext, validationContext.Items);
                            result = TryValidateObjectRecursive(item, nestedContext, results) && result;
                        }
                    }
                }
                else if (property.PropertyType.IsClass && property.PropertyType != typeof(string))
                {
                    var nestedContext = new ValidationContext(value, validationContext, validationContext.Items);
                    result = TryValidateObjectRecursive(value, nestedContext, results) && result;
                }
            }

            return result;
        }

        public static Dictionary<string, string[]> GetValidationErrorsDictionary(List<ValidationResult> results)
        {
            return results
                .SelectMany(vr => vr.MemberNames.Select(memberName => new { memberName, ErrorMessage = vr.ErrorMessage ?? string.Empty }))
                .GroupBy(x => x.memberName, x => x.ErrorMessage)
                .ToDictionary(g => g.Key, g => g.ToArray());
        }
    }

    public abstract class NumberOfChoicesValidatorAttribute : ValidationAttribute
    {
        protected readonly string CandidatesPropertyName;
        protected readonly string RacePropertyName;
        protected string RacePropertyValue = string.Empty;

        protected NumberOfChoicesValidatorAttribute(string propertyName, string racePropertyName)
        {
            CandidatesPropertyName = propertyName;
            RacePropertyName = racePropertyName;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            // Get the property info of the property
            var candidatePropertyInfo = validationContext.ObjectType.GetProperty(CandidatesPropertyName);
            if (candidatePropertyInfo == null)
            {
                return new ValidationResult($"Property not found.", [validationContext.MemberName]);
            }

            // Get the value of the property
            var candidatePropertyValue = candidatePropertyInfo.GetValue(validationContext.ObjectInstance);
            if (candidatePropertyValue is not List<CandidateModel>)
            {
                return new ValidationResult($"Property '{CandidatesPropertyName}' is not a valid List<CandidateModel> type.", [validationContext.MemberName]);
            }

            // Get the value of the Name Property
            var nameProperty = validationContext.ObjectType.GetProperty(RacePropertyName);
            if (nameProperty != null)
            {
                RacePropertyValue = nameProperty.GetValue(validationContext.ObjectInstance).ToString();
            }

            // If not a Ballot, then no need to validate. Get out.
            // This isn't the best way of doing this. It's a bit of a hack. The issue is that we want to use the Election model
            // both for creating an election and ballot submission. This is optimal for model re-use. But the data annotations
            // work for ballot submission, are too tight for election creation. So this flag gets around that. When creating an election
            // if the "IsBallot" flag isn't set, the validator simply allows the validation to proceed.
            var isBallot = validationContext.Items.Where(i => i.Key.ToString() == "IsBallot");
            if (!isBallot.Any())
            {
                return ValidationResult.Success;
            }

            // Calculate the number of selections in the candidate choices
            var selectedCount = ((IEnumerable) candidatePropertyValue).Cast<CandidateModel>().Where(c => c.Selected == true).Count();

            return ValidateCount(value, selectedCount, validationContext);
        }

        protected abstract ValidationResult ValidateCount(object value, int count, ValidationContext validationContext);
    }

    public class MaxNumberOfChoicesValidatorAttribute : NumberOfChoicesValidatorAttribute
    {
        public MaxNumberOfChoicesValidatorAttribute(string propertyName, string racePropertyName) : base(propertyName, racePropertyName)
        {
        }

        protected override ValidationResult ValidateCount(object value, int selectedCount, ValidationContext validationContext)
        {
            var maxNumberOfChoices = value as int?;
            if (maxNumberOfChoices.HasValue && (selectedCount > maxNumberOfChoices.Value))
            {
                return new ValidationResult($"Number of selected items in '{CandidatesPropertyName}' cannot exceed MaxNumberOfChoices for '{RacePropertyValue}'. MaxNumberOfChoices: {maxNumberOfChoices}, SelectedCount: {selectedCount}", [validationContext.MemberName]);
            }

            return ValidationResult.Success;
        }
    }

    public class MinNumberOfChoicesValidatorAttribute : NumberOfChoicesValidatorAttribute
    {
        public MinNumberOfChoicesValidatorAttribute(string propertyName, string racePropertyName) : base(propertyName, racePropertyName)
        {
        }

        protected override ValidationResult ValidateCount(object value, int selectedCount, ValidationContext validationContext)
        {
            var minNumberOfChoices = value as int?;
            if (minNumberOfChoices.HasValue && (selectedCount < minNumberOfChoices.Value))
            {
                return new ValidationResult($"Number of selected items in '{CandidatesPropertyName}' must be greater or equal to MinNumberOfChoices for '{RacePropertyValue}'. MinNumberOfChoices: {minNumberOfChoices}, Count: {selectedCount}", [validationContext.MemberName]);
            }

            return ValidationResult.Success;
        }
    }

    public class BallotIntegrityChecker : ValidationAttribute
    {
        protected readonly string _electionPropertyName;

        public BallotIntegrityChecker(string electionPropertyName)
        {
            _electionPropertyName = electionPropertyName;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var electionPropertyInfo = validationContext.ObjectType.GetProperty(_electionPropertyName);
            if (electionPropertyInfo == null)
            {
                return new ValidationResult($"Property not found.", [validationContext.MemberName]);
            }

            // Get the value of the property
            var electionPropertyValue = electionPropertyInfo.GetValue(validationContext.ObjectInstance);
            if (electionPropertyValue is not ElectionModel)
            {
                return new ValidationResult($"Property '{_electionPropertyName}' is not a valid ElectionModel type.", [validationContext.MemberName]);
            }
            var election = (ElectionModel) electionPropertyValue;

            // Get the DB Context
            var trueVoteDbContext = (ITrueVoteDbContext) validationContext.GetService(typeof(ITrueVoteDbContext));
            if (trueVoteDbContext == null)
            {
                // Try and get it from the Items context.
                trueVoteDbContext = validationContext.Items["DBContext"] as ITrueVoteDbContext;
                if (trueVoteDbContext == null)
                {
                    return new ValidationResult($"Could not get DBContext for Property '{_electionPropertyName}'.", [validationContext.MemberName]);
                }
            }

            // Try and get the election from the DB
            var electionFromDBSet = trueVoteDbContext.Elections.Where(e => e.ElectionId == election.ElectionId);

            // See if the Election exists
            var electionCount = electionFromDBSet.Count();
            if (electionCount == 0)
            {
                return new ValidationResult($"Ballot for Election: {election.ElectionId} is invalid. Election not found.", [validationContext.MemberName]);
            }

            // Confirm ballot is within election start / end date.
            var electionFromDB = electionFromDBSet.FirstOrDefault();
            var now = UtcNowProviderFactory.GetProvider().UtcNow;
            var ballotWithinElectionDateTime = now >= electionFromDB.StartDate && now <= electionFromDB.EndDate;
            if (!ballotWithinElectionDateTime)
            {
                return new ValidationResult($"Ballot for Election: {election.ElectionId} is invalid. Submitted at: {now}, which is outside the election start: {electionFromDB.StartDate} and election end: {electionFromDB.EndDate}.", [validationContext.MemberName]);
            }

            // Confirm the selection flag is set on all candidates.
            var nullSelections = election.Races.Where(r => r != null).SelectMany(r => r.Candidates).Where(c => c != null).Count(c => c.Selected == null);
            if (nullSelections > 0)
            {
                return new ValidationResult($"Ballot contains {nullSelections} null candidate selections. They must all be true or false.", [validationContext.MemberName]);
            }

            return ValidationResult.Success;
        }
    }
}
#pragma warning restore IDE0046 // Convert to conditional expression
