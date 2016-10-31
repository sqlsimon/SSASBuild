using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Xml;
using System.Xml.Xsl;
using Microsoft.AnalysisServices;
using System.IO;
using System.Diagnostics;

namespace SsasHelper
{
    /// <summary>
    /// This class contains methods to work with SSAS Projects.  
    /// </summary>
    /// <remarks>
    /// Author:  DDarden
    /// Date  :  200905031313
    /// Use this at your own risk.  It has been tested and runs successfully, but results may vary.
    /// Works on my machine... :)
    /// 
    /// KNOWN ISSUES
    /// - Partitions are reordered when De-Serialized/Serialized.  Makes it a pain to validate,
    ///   but I've seen no ill effects.
    /// </remarks>
    public static class ProjectHelper
    {
        /// <summary>
        /// Load a SSAS project file into a SSAS Database
        /// </summary>
        /// <remarks>
        /// TODO:  Doesn't support Assemblies, or possibly some other types
        /// </remarks>
        /// <param name="ssasProjectFile">Path to the .dwproj file for a SSAS Project</param>
        /// <returns>AMO Database built from the SSAS project file</returns>
        public static Database DeserializeProject(string ssasProjectFile)
        {
            Database database = new Database();
            XmlReader innerReader = null;
            XmlNodeList nodeList = null;
            XmlNodeList dependencyNodeList = null;
            string fullPath = null;
                    
            // Verify inputs
            if (!File.Exists(ssasProjectFile))
            {
                throw new ArgumentException(string.Format("'{0}' does not exist", ssasProjectFile));
            }

            // Get the directory to load all project files
            FileInfo fi = new FileInfo(ssasProjectFile);
            string ssasProjectDirectory = fi.Directory.FullName + "\\";
            
            // Load the SSAS Project File
            XmlReader reader = new XmlTextReader(ssasProjectFile);
            XmlDocument doc = new XmlDocument();
            doc.Load(reader);

            // Load the Database
            nodeList = doc.SelectNodes("//Database/FullPath");
            fullPath = nodeList[0].InnerText;
            innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
            Utils.Deserialize(innerReader, (MajorObject)database);

            // Load all the Datasources
            nodeList = doc.SelectNodes("//DataSources/ProjectItem/FullPath");
            DataSource dataSource = null;
            foreach (XmlNode node in nodeList)
            {
                fullPath = node.InnerText;
                innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
                dataSource = new RelationalDataSource();
                Utils.Deserialize(innerReader, (MajorObject)dataSource);
                database.DataSources.Add(dataSource);
            }

            // Load all the DatasourceViews
            nodeList = doc.SelectNodes("//DataSourceViews/ProjectItem/FullPath");
            DataSourceView dataSourceView = null;
            foreach (XmlNode node in nodeList)
            {
                fullPath = node.InnerText;
                innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
                dataSourceView = new DataSourceView();
                Utils.Deserialize(innerReader, (MajorObject)dataSourceView);
                database.DataSourceViews.Add(dataSourceView);
            }

            // Load all the Roles
            nodeList = doc.SelectNodes("//Roles/ProjectItem/FullPath");
            Role role = null;
            foreach (XmlNode node in nodeList)
            {
                fullPath = node.InnerText;
                innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
                role = new Role();
                Utils.Deserialize(innerReader, (MajorObject)role);
                database.Roles.Add(role);
            }

            // Load all the Dimensions
            nodeList = doc.SelectNodes("//Dimensions/ProjectItem/FullPath");
            Dimension dimension = null;
            foreach (XmlNode node in nodeList)
            {
                fullPath = node.InnerText;
                innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
                dimension = new Dimension();
                Utils.Deserialize(innerReader, (MajorObject)dimension);
                database.Dimensions.Add(dimension);
            }

            // Load all the Mining Models
            nodeList = doc.SelectNodes("//MiningModels/ProjectItem/FullPath");
            MiningStructure miningStructure = null;
            foreach (XmlNode node in nodeList)
            {
                fullPath = node.InnerText;
                innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
                miningStructure = new MiningStructure();
                Utils.Deserialize(innerReader, (MajorObject)miningStructure);
                database.MiningStructures.Add(miningStructure);
            }
                        
            // Load all the Cubes
            nodeList = doc.SelectNodes("//Cubes/ProjectItem/FullPath");
            Cube cube = null;
            foreach (XmlNode node in nodeList)
            {
                fullPath = node.InnerText;
                innerReader = new XmlTextReader(ssasProjectDirectory + fullPath);
                cube = new Cube();
                Utils.Deserialize(innerReader, (MajorObject)cube);
                database.Cubes.Add(cube);

                // Process cube dependencies (i.e. partitions
                // Little known fact:  The Serialize/Deserialize methods DO handle partitions... just not when 
                // paired with anything else in the cube.  We have to do this part ourselves
                dependencyNodeList = node.ParentNode.SelectNodes("Dependencies/ProjectItem/FullPath");
                foreach (XmlNode dependencyNode in dependencyNodeList)
                {
                    fullPath = dependencyNode.InnerText;
                    innerReader = ProjectHelper.FixPartitionsFileForDeserialize( ssasProjectDirectory + fullPath, cube);
                    Cube partitionsCube = new Cube();
                    Utils.Deserialize(innerReader, (MajorObject)partitionsCube);
                    MergePartitionCube(cube, partitionsCube);
                }
            }

            return database;
        }

        /// <summary>
        /// Save a Database object to the individual BIDS files.
        /// The filename for each object will be based on its Name.
        /// </summary>
        /// <remarks>
        /// TODO:  Doesn't support Assemblies, or possibly some other types
        /// 
        /// Some attributes are re-ordered in DSVs.
        /// 
        /// The following information will be lost when saving a project:
        /// (these are not required for correct operation)
        /// - State (Processed, Unprocessed)
        /// - CreatedTimestamp
        /// - LastSchemaUpdate
        /// - LastProcessed
        /// - dwd:design-time-name
        /// - CurrentStorageMode
        /// </remarks>
        /// <param name="database">Database to output files for</param>
        /// <param name="targetDirectory">Directory to create the files in</param>
        public static void SerializeProject(Database database, string targetDirectory)
        {
            // Validate inputs
            if (database == null)
            {
                throw new ArgumentException("Please provide a database object");
            }

            if (string.IsNullOrEmpty(targetDirectory))
            {
                throw new ArgumentException("Please provide a directory to write the files to");
            }

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (!targetDirectory.EndsWith("\\")) { targetDirectory += "\\"; }

            XmlTextWriter writer = null;

            // Iterate through all objects in the database and serialize them
            foreach (DataSource dataSource in database.DataSources)
            {
                writer = new XmlTextWriter(targetDirectory + dataSource.Name + ".ds", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)dataSource, false);
                writer.Close();
            }

            foreach (DataSourceView dataSourceView in database.DataSourceViews)
            {
                writer = new XmlTextWriter(targetDirectory + dataSourceView.Name + ".dsv", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)dataSourceView, false);
                writer.Close();
            }

            foreach (Role role in database.Roles)
            {
                writer = new XmlTextWriter(targetDirectory + role.Name + ".role", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)role, false);
                writer.Close();
            }

            foreach (Dimension dimension in database.Dimensions)
            {
                writer = new XmlTextWriter(targetDirectory + dimension.Name + ".dim", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)dimension, false);
                writer.Close();
            }

            foreach (MiningStructure miningStructure in database.MiningStructures)
            {
                writer = new XmlTextWriter(targetDirectory + miningStructure.Name + ".dmm", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)miningStructure, false);
                writer.Close();
            }

            // Special case:  The cube serialization won't work for partitions when Partion/AggregationDesign
            // objects are mixed in with other objects.  Serialize most of the cube, then split out
            // Partion/AggregationDesign objects into their own cube to serialize, then clean up
            // a few tags
            foreach (Cube cube in database.Cubes)
            {
                writer = new XmlTextWriter(targetDirectory + cube.Name + ".cube", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)cube, false);
                writer.Close();

                // Partitions and AggregationDesigns may be written to the Cube file, and we want
                // to keep them all in the Partitions file; strip them from the cube file
                FixSerializedCubeFile(targetDirectory + cube.Name + ".cube");

                Cube partitionCube = SplitPartitionCube(cube);
                writer = new XmlTextWriter(targetDirectory + cube.Name + ".partitions", Encoding.UTF8);
                writer.Formatting = Formatting.Indented;
                Utils.Serialize(writer, (MajorObject)partitionCube, false);
                writer.Close();

                // The partitions file gets serialized with a few extra nodes... remove them
                FixSerializedPartitionsFile(targetDirectory + cube.Name + ".partitions");
            }
        }

        /// <summary>
        /// Generate a .ASDatabase file based on a database object.
        /// </summary>
        /// <remarks>
        /// The following information will be lost from the .ASDatabase file
        /// - State (Processed, Unprocessed)
        /// - CreatedTimestamp
        /// - LastSchemaUpdate
        /// - LastProcessed
        /// - dwd:design-time-name
        /// - CurrentStorageMode
        /// </remarks>
        /// <param name="database">Database to build</param>
        /// <param name="targetFilename">File to generate</param>
        public static void GenerateASDatabaseFile(Database database, string targetFilename)
        {
            // Validate inputs
            if (database == null)
            {
                throw new ArgumentException("Please provide a database object");
            }

            // Create the directory to put the file in if it doesn't exist
            FileInfo fi = new FileInfo(targetFilename);
            if (!fi.Directory.Exists)
            {
                fi.Directory.Create();
            }

            // Build the ASDatabase file...
            XmlTextWriter writer = new XmlTextWriter(targetFilename, Encoding.UTF8);
            writer.Formatting = Formatting.Indented;
            Utils.Serialize(writer, (MajorObject)database, false);
            writer.Close();
        }

        /// <summary>
        /// Generate a .ASDatabase file based on a database object.
        /// </summary>
        /// <remarks>
        /// The following information will be lost from the .ASDatabase file
        /// - State (Processed, Unprocessed)
        /// - CreatedTimestamp
        /// - LastSchemaUpdate
        /// - LastProcessed
        /// - dwd:design-time-name
        /// - CurrentStorageMode
        /// </remarks>
        /// <param name="ssasProjectFile">Path the the .dwproj file for a SSAS Project</param>
        /// <param name="targetFilename">File to generate</param>
        public static void GenerateASDatabaseFile(string ssasProjectFile, string targetFilename)
        {
            Database database;

            database = ProjectHelper.DeserializeProject(ssasProjectFile);

            ProjectHelper.GenerateASDatabaseFile(database, targetFilename);
        }

        /// <summary>
        /// Clean non-essential, highly volatile attributes from SSAS project files.
        /// </summary>
        /// <remarks>
        /// Clean only the top level directory, using the default files, creating backups,
        /// not removing dimension annotations,not removing design-time-name, and throwing away file counts
        /// </remarks>
        /// <param name="ssasProjectPath">SSAS project directory</param>
        public static void CleanSsasProjectDirectory(string ssasProjectPath)
        {
            int filesInspectedCount = 0;
            int filesAlteredCount = 0;
            int filesCleanedCount = 0;

            // Clean the SSAS directory
            // Only the top level directory, using the default files, creating backups, not removing dimension annotations,
            // not removing design-time-name, and throwing away file counts
            CleanSsasProjectDirectory(ssasProjectPath, string.Empty,SearchOption.TopDirectoryOnly, false, false,true
                , out filesInspectedCount, out filesCleanedCount, out filesAlteredCount);
        }

        /// <summary>
        /// Clean non-essential, highly volatile attributes from SSAS project files.
        /// </summary>
        /// <remarks>
        /// Clean the directory(ies) with a higher degree of control.
        /// </remarks>
        /// <param name="ssasProjectPath">SSAS project directory</param>
        /// <param name="searchPatterns">A CSV list of files to inspect; use an empty string to use "*.cube,*.partitions,*.dsv,*.dim,*.ds,*.role"</param>
        /// <param name="searchOption">Top level directory only or entire subtree</param>
        /// <param name="removeDesignTimeNames">Remove "design-time-name" attributes?  These are regenerated each time the SSAS solution is created, reverse engineered from a server, etc.</param>
        /// <param name="removeDimensionAnnotations">Remove annotations from dimensions?</param>
        /// <param name="createBackup">Create a backup of any file that is modified?</param>
        /// <param name="filesInspectedCount">Number of files that were inspected based on the search pattern</param>
        /// <param name="filesCleanedCount">Number of writeable files that were analyzed</param>
        /// <param name="filesAlteredCount">Number of files whose contents were modified</param>
        public static void CleanSsasProjectDirectory(string ssasProjectPath, string searchPatterns, SearchOption searchOption
            ,bool removeDesignTimeNames, bool removeDimensionAnnotations, bool createBackup
            ,out int filesInspectedCount, out int filesCleanedCount, out int filesAlteredCount)
        {
            // Validate inputs
            if(!Directory.Exists(ssasProjectPath))
            {
                throw new ArgumentException("Please provide SSAS project directory");
            }

            // Provide the default list of file extensions
            if (searchPatterns.Trim().Length == 0) { searchPatterns = "*.cube,*.partitions,*.dsv,*.dim,*.ds,*.dmm,*.role"; }
	        
            // Keep track of the changes that we're making
            filesInspectedCount = 0;
	        filesCleanedCount = 0;
            filesAlteredCount = 0;
            bool fileChanged = false;

            foreach (string searchPattern in searchPatterns.Split(",".ToCharArray()))
            {
                // Iterate over all the files that match the search pattern
                foreach (string filename in Directory.GetFiles(ssasProjectPath, searchPattern, searchOption))
                {
                    fileChanged = false;
                    ++filesInspectedCount;

                    // Check if the file is Read-only; ignore if so
                    if ((File.GetAttributes(filename) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        Debug.WriteLine(string.Format("{0} is read-only; skipping", filename));
                        continue;
                    }

                    ++filesCleanedCount;

                    // Load the file we're cleaning
                    XmlDocument document = new XmlDocument();
                    document.Load(filename);

                    // Prepare for our XPath queries
                    XmlNamespaceManager xmlnsManager = LoadSsasNamespaces(document);

                    XmlNodeList nodeList;

                    // Clean the elements in the SSAS files that are volatile but unimportant
                    // Stripping these out makes comparing, merging, and analyzing the source files
                    // substantially easier.  These fields are not required for correct operation.
                    nodeList = document.SelectNodes("//AS:CreatedTimestamp", xmlnsManager);
                    if (XmlHelper.RemoveNodes(nodeList) > 0) { fileChanged = true; }
                    nodeList = document.SelectNodes("//AS:LastSchemaUpdate", xmlnsManager);
                    if (XmlHelper.RemoveNodes(nodeList) > 0) { fileChanged = true; }
                    nodeList = document.SelectNodes("//AS:LastProcessed", xmlnsManager);
                    if (XmlHelper.RemoveNodes(nodeList) > 0) { fileChanged = true; }
                    nodeList = document.SelectNodes("//AS:State", xmlnsManager);
                    if (XmlHelper.RemoveNodes(nodeList) > 0) { fileChanged = true; }
                    nodeList = document.SelectNodes("//AS:CurrentStorageMode", xmlnsManager);
                    if (XmlHelper.RemoveNodes(nodeList) > 0) { fileChanged = true; }

                    // Dimension annotations don't tend to be as volatile as other annotations
                    // We might not want to remove them
                    if ((removeDimensionAnnotations) || (!filename.EndsWith(".dim")))
                    {
                        nodeList = document.SelectNodes("//AS:Annotations", xmlnsManager);
                        if (XmlHelper.RemoveNodes(nodeList) > 0) { fileChanged = true; }
                    }

                    // Remove the 'design-time-name' element, which is regenerated each time the
                    // SSAS project files are regenerated.  This element is not required.
                    if (removeDesignTimeNames)
                    {
                        nodeList = document.SelectNodes("//@dwd:design-time-name/..", xmlnsManager);
                        if (XmlHelper.RemoveAttributes(nodeList, "dwd:design-time-name") > 0) { fileChanged = true; }
                        nodeList = document.SelectNodes("//@msprop:design-time-name/..", xmlnsManager);
                        if (XmlHelper.RemoveAttributes(nodeList, "msprop:design-time-name") > 0) { fileChanged = true; }
                    }

                    // If we actually changed the file, update the count and save it
                    if (fileChanged)
                    {
                        ++filesAlteredCount;
                        Debug.WriteLine(string.Format("Altered '{0}'", filename));

                        // Create a backup of the file since we're mucking with it
                        if (createBackup)
                        {
                            string backupFilename = filename + "." + DateTime.Now.ToString("yyyyMMddHHmm") + ".bak";
                            File.Copy(filename, backupFilename);
                        }

                        document.Save(filename);
                    }

                    Debug.Write(string.Format("Cleaned '{0}'", filename));
                }
            }
        			
        	Debug.WriteLine(string.Format("Inspected {0:n0} files", filesInspectedCount));
            Debug.WriteLine(string.Format("Cleaned {0:n0} files", filesCleanedCount));
            Debug.WriteLine(string.Format("Altered {0:n0} files", filesAlteredCount));
        }


        /// <summary>
        /// Transform the XML that makes up a given SSAS project file to sort the elements,
        /// and remove non-essential attributes.  This makes it easy to compare and validate
        /// files.
        /// </summary>
        /// <param name="inputFilename">File to read and transform</param>
        /// <param name="outputFilename">Transformed file to output</param>
        public static void SortSsasFile(string inputFilename, string outputFilename)
        {
            // Validate inputs
            if (!File.Exists(inputFilename))
            {
                throw new ArgumentException(string.Format("'{0}' does not exist", inputFilename));
            }

            // Load an XML document to transform
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(inputFilename);
            
            // Load the XSLT to sort the file; pull from a property into an XmlReader
            string xslt = SsasHelper.Properties.Resources.SsasSortXslt;
            StringReader xsltReader = new StringReader(xslt);
            XmlReader xmlXslt = XmlReader.Create(xsltReader);

            // Create the transform
            XslCompiledTransform xslTran = new XslCompiledTransform();
            xslTran.Load(xmlXslt);

            // Create a TextWriter to output the Transformed XML document so it will be correctly formated
            XmlTextWriter output = new XmlTextWriter(outputFilename, System.Text.Encoding.UTF8);
            output.Formatting = Formatting.Indented;

            // Transform and output
            xslTran.Transform(xmlDoc, null, output);
            output.Close();
        }

        /// <summary>
        /// Validates the database.  This returns the errors encountered by building.
        /// </summary>
        /// <param name="database">The database object to validate.</param>
        /// <param name="serverEdition">Edition of SQL Server to validate against (Enterprise, Standard, Developer).</param>
        /// <param name="results">Collection of validation results to rbe returned.</param>
        /// <returns>True for 'No Errors', False for 'Error Present'.</returns>
        public static bool ValidateDatabase(Database database, string serverEdition, out ValidationResultCollection results)
        {
            bool doesBuild = false;
            results = new ValidationResultCollection();

            // Validate the Server Edition string
            if (!Enum.IsDefined(typeof(ServerEdition), serverEdition))
            {
                throw new ArgumentException(string.Format("'{0}' is not a valid ServerEdition.", serverEdition));
            }

            ServerEdition edition = (ServerEdition)Enum.Parse(typeof(ServerEdition), serverEdition, true);
            
            // We have to provide a ServerEdition for this method to work.  There are 
            // overloads that look like the will work without them, but they can't be used
            // in this scenario.
            // This can be modified to return warnings and messages as well.
            doesBuild = database.Validate(results, ValidationOptions.None, edition);

            return doesBuild;
        }

        #region Private Helper Methods
        /// <summary>
        /// Split out the Partitions and AggregationDesignss from a base cube
        /// into their own cube for deserialization into a Partitions file
        /// </summary>
        /// <param name="baseCube">Cube to split</param>
        /// <returns>Cube containing only partitions and aggregations</returns>
        private static Cube SplitPartitionCube(Cube baseCube)
        {
            Cube partitionCube = new Cube();

            foreach(MeasureGroup mg in baseCube.MeasureGroups)
            {
                MeasureGroup newMG = new MeasureGroup(mg.Name, mg.ID);

                if ((mg.Partitions.Count == 0) && (mg.AggregationDesigns.Count == 0))
                    continue;
                
                partitionCube.MeasureGroups.Add(newMG);

                // Heisenberg principle in action with these objects; use 'for' instead of 'foreach'
                if (mg.Partitions.Count > 0)
                {
                    for (int i = 0; i < mg.Partitions.Count; ++i)
                    {
                        Partition partitionCopy = mg.Partitions[i].Clone();
                        newMG.Partitions.Add(partitionCopy);
                    }
                }

                // Heisenberg principle in action with these objects; use 'for' instead of 'foreach'
                if (mg.AggregationDesigns.Count > 0)
                {
                    for (int i = 0; i < mg.AggregationDesigns.Count; ++i)
                    {
                        AggregationDesign aggDesignCopy = mg.AggregationDesigns[i].Clone();
                        newMG.AggregationDesigns.Add(aggDesignCopy);
                    }
                }
            }

            return partitionCube;
        }

        /// <summary>
        /// Merge a cube containing only Partitions and AggregationDesigns with
        /// a 'base cube' that contains all Measure Groups present in the 'partition cube'.
        /// </summary>
        /// <param name="baseCube">A fully populated cube</param>
        /// <param name="partitionCube">A cube containing only partitions and aggregation designs</param>
        private static void MergePartitionCube(Cube baseCube, Cube partitionCube)
        {
            MeasureGroup baseMG = null;

            foreach (MeasureGroup mg in partitionCube.MeasureGroups)
            {
                baseMG = baseCube.MeasureGroups.Find(mg.ID);

                // Heisenberg principle in action with these objects; use 'for' instead of 'foreach'
                if (mg.Partitions.Count > 0)
                {
                    for (int i = 0; i < mg.Partitions.Count; ++i)
                    {
                        Partition partitionCopy = mg.Partitions[i].Clone();
                        baseMG.Partitions.Add(partitionCopy);
                    }
                }

                // Heisenberg principle in action with these objects; use 'for' instead of 'foreach'
                if (mg.AggregationDesigns.Count > 0)
                {
                    for (int i = 0; i < mg.AggregationDesigns.Count; ++i)
                    {
                        AggregationDesign aggDesignCopy = mg.AggregationDesigns[i].Clone();
                        baseMG.AggregationDesigns.Add(aggDesignCopy);
                    }
                }
            }
        }

        /// <summary>
        /// Load a .Partitions file and the cube object it belongs
        /// to.  It will add a <Name></Name> node (populated by matching the Partitions
        /// MeasureGroup ID to the cube's MeasureGroup ID).
        /// </summary>
        /// <param name="partitionFilename">Name of the partitions file</param>
        /// <param name="sourceCube">Cube the .partitions file belongs to</param>
        /// <returns>XmlReader containing the file</returns>
        private static XmlReader FixPartitionsFileForDeserialize(string partitionFilename,Cube sourceCube)
        {
            // Validate inputs
            if (sourceCube == null)
            {
                throw new ArgumentException("Provide a Cube object that matches the partitions file");
            }

            if (string.IsNullOrEmpty(partitionFilename))
            {
                throw new ArgumentException("Provide a partitions file");
            }
            // I am NOT validating the extention to provide some extra flexibility here

            XmlDocument document = new XmlDocument();
            document.Load(partitionFilename);

            // Setup for XPath queries
            XmlNamespaceManager xmlnsManager = LoadSsasNamespaces(document);
            string defaultNamespaceURI = "http://schemas.microsoft.com/analysisservices/2003/engine";
             
            // Get all the MeasureGroup IDs
            XmlNodeList nodeList = document.SelectNodes("/AS:Cube/AS:MeasureGroups/AS:MeasureGroup/AS:ID", xmlnsManager);
            XmlNode newNode = null;
            
            // Add a Name node underneath the ID node if one doesn't exist, using the MeasureGroup's real name
            foreach (XmlNode node in nodeList)
            {
                // Verify the node doesn't exist
                if (XmlHelper.NodeExists(node.ParentNode, "Name"))
                    continue;

                newNode = document.CreateNode(XmlNodeType.Element, "Name", defaultNamespaceURI);
                // Lookup the MG name from the cube based on the ID in the file
                newNode.InnerText = sourceCube.MeasureGroups.Find(node.InnerText).Name;
                node.ParentNode.InsertAfter (newNode, node);
            }

            // Return this as an XmlReader, so it can be manipulated
            return new XmlTextReader(new StringReader(document.OuterXml));
        }

        /// <summary>
        /// Remove the <Name></Name> nodes stored by the serialize method that
        /// don't belong.
        /// </summary>
        /// <param name="partitionFilename">Name of the partitions file</param>
        private static void FixSerializedPartitionsFile(string partitionFilename)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(partitionFilename))
            {
                throw new ArgumentException("Provide a partitions file");
            }
            // I am NOT validating the extention to provide some extra flexibility here

            XmlDocument document = new XmlDocument();
            document.Load(partitionFilename);

            XmlNamespaceManager xmlnsManager = LoadSsasNamespaces(document);

            XmlNodeList nodeList = null;
            
            // Remove the MeasureGroup Names
            nodeList = document.SelectNodes("/AS:Cube/AS:MeasureGroups/AS:MeasureGroup/AS:Name", xmlnsManager);
            XmlHelper.RemoveNodes(nodeList);

            // Remove the StorageModes
            nodeList = document.SelectNodes("/AS:Cube/AS:MeasureGroups/AS:MeasureGroup/AS:StorageMode", xmlnsManager);
            XmlHelper.RemoveNodes(nodeList);

            // Remove the ProcessingModes
            nodeList = document.SelectNodes("/AS:Cube/AS:MeasureGroups/AS:MeasureGroup/AS:ProcessingMode", xmlnsManager);
            XmlHelper.RemoveNodes(nodeList);

            document.Save(partitionFilename);
        }

        /// <summary>
        /// Remove the <Partitions></Partitions> and <AggregationDesigns></AggregationDesigns>
        /// elements from the Cube file if they exists; these will be serialized in the Partitions file.
        /// </summary>
        /// <param name="cubeFilename">Name of the cube file to</param>
        private static void FixSerializedCubeFile(string cubeFilename)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(cubeFilename))
            {
                throw new ArgumentException("Provide a cube file");
            }
            // I am NOT validating the extention to provide some extra flexibility here

            XmlDocument document = new XmlDocument();
            document.Load(cubeFilename);

            XmlNamespaceManager xmlnsManager = LoadSsasNamespaces(document);

            XmlNodeList nodeList = null;

            // Remove the MeasureGroup Names
            nodeList = document.SelectNodes("/AS:Cube/AS:MeasureGroups/AS:MeasureGroup/AS:Partitions", xmlnsManager);
            XmlHelper.RemoveNodes(nodeList);

            // Remove the StorageModes
            nodeList = document.SelectNodes("/AS:Cube/AS:MeasureGroups/AS:MeasureGroup/AS:AggregationDesigns", xmlnsManager);
            XmlHelper.RemoveNodes(nodeList);

            document.Save(cubeFilename);
        }

        /// <summary>
        /// Load the SSAS namespaces into a XmlNameSpaceManager.  These are used for XPath
        /// queries into a SSAS file
        /// </summary>
        /// <param name="document">XML Document to load namespaces for</param>
        /// <returns>XmlNamespaceManager loaded with SSAS namespaces</returns>
        private static XmlNamespaceManager LoadSsasNamespaces(XmlDocument document)
        {
            XmlNamespaceManager xmlnsManager = new System.Xml.XmlNamespaceManager(document.NameTable);
            //xmlns:xsd="http://www.w3.org/2001/XMLSchema" 
            xmlnsManager.AddNamespace("xsd", "http://www.w3.org/2001/XMLSchema");
            //xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
            xmlnsManager.AddNamespace("xsi", "http://www.w3.org/2001/XMLSchema-instance");
            //xmlns:ddl2="http://schemas.microsoft.com/analysisservices/2003/engine/2" 
            xmlnsManager.AddNamespace("ddl2", "http://schemas.microsoft.com/analysisservices/2003/engine/2");
            //xmlns:ddl2_2="http://schemas.microsoft.com/analysisservices/2003/engine/2/2" 
            xmlnsManager.AddNamespace("ddl2_2", "http://schemas.microsoft.com/analysisservices/2003/engine/2/2");
            //xmlns:ddl100_100="http://schemas.microsoft.com/analysisservices/2008/engine/100/100" 
            xmlnsManager.AddNamespace("ddl100_100", "http://schemas.microsoft.com/analysisservices/2008/engine/100/100");
            //xmlns:dwd="http://schemas.microsoft.com/DataWarehouse/Designer/1.0" 
            xmlnsManager.AddNamespace("dwd", "http://schemas.microsoft.com/DataWarehouse/Designer/1.0");
            //xmlns="http://schemas.microsoft.com/analysisservices/2003/engine"
            xmlnsManager.AddNamespace("AS", "http://schemas.microsoft.com/analysisservices/2003/engine");
            xmlnsManager.AddNamespace("msprop", "urn:schemas-microsoft-com:xml-msprop");
            xmlnsManager.AddNamespace("xs", "http://www.w3.org/2001/XMLSchema");
            xmlnsManager.AddNamespace("msdata", "urn:schemas-microsoft-com:xml-msdata");

            return xmlnsManager;
        }
        #endregion
    }
}
