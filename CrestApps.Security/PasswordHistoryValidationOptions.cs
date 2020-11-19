namespace CrestApps.Security
{
    public class PasswordHistoryValidationOptions
    {
        /// <summary>
        /// Total days to prevent the user from reusing passwords. This value cannot be negative value. Negative values will be converted to absolute value.
        /// </summary>
        public int TotalDays { get; set; } = 365;

        /// <summary>
        /// The error code to give when the validation fails
        /// </summary>
        public string ErrorCode { get; set; } = "1";

        /// <summary>
        /// The remplate to use for generating the error message. use {0} to place the TotalDays Value.
        /// </summary>
        public string ErrorCodeTemplate { get; set; } = "The given password must not have been used in the past {0} days";
    }
}
