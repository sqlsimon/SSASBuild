using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AnalysisServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SsasHelper;

namespace SsasBuilder
{
    /// <summary>
    /// MSBuild task to create a .ASDatabase file from a Visual Studio SSAS project.
    /// The task will de-serialize the project, validate it against a Server Edition,
    /// and create the .ASDatabase file if the project is validated successfully.
    /// </summary>
    public class SsasBuildASDatabaseFileTask : Task
    {
        #region Public Properties
        /// <summary>
        /// SSAS Project File to de-serialize and compile.
        /// </summary>
        [Required]
        public string SsasProjectFile { get; set; }

        /// <summary>
        /// Name and location of the .ASDatabase file to write.
        /// </summary>
        [Required]
        public string SsasTargetFile { get; set; }

        /// <summary>
        /// Server Edition of the target server.  Required for accurate validation of the project.
        /// Enterprise, Developer, Standard
        /// </summary>
        [Required]
        public string SsasServerEdition { get; set; }
        #endregion

        #region Public Methods
        public override bool Execute()
        {
            Log.LogMessage(MessageImportance.Normal, "Project File  :  {0}", SsasProjectFile);
            Log.LogMessage(MessageImportance.Normal, "Target File   :  {0}", SsasTargetFile);
            Log.LogMessage(MessageImportance.Normal, "Server Edition:  {0}", SsasServerEdition);

            // Validate Project File Exists
            if (!File.Exists(SsasProjectFile))
            {
                throw new FileNotFoundException(string.Format("'{0}' does not exist.", SsasProjectFile));
            }

            try
            {
                Database database = ProjectHelper.DeserializeProject(SsasProjectFile);

                // ... Verify our project doesn't have any errors ...
                ValidationResultCollection results;

                bool isValidated = ProjectHelper.ValidateDatabase(database, SsasServerEdition, out results);

                // If the database doesn't validate (i.e., a build error)
                // log the errors and return failure.
                foreach (ValidationResult result in results)
                {
                    Log.LogError(result.Description);
                }

                if (!isValidated)
                {
                    return false;
                }

                // Build the .ASDatabase file
                ProjectHelper.GenerateASDatabaseFile(database, SsasTargetFile);
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex);
                return false;
            }

            return true;
        }
        #endregion
    }
}
