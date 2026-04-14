1. - done - When we create a project and then we need to set the project for all other activities like BRD generation. Code development bug fix etc. so now provide this capability. In the UI user needs to set the project. if you need more info ask questions i will clarify.

2. - done - Reqs Reader — Agent should read the BRD insted of file path. remove that file path option for this Agent.

3.- done - Requiremtns uploads and BRD generation will also be per project.

4. - done - I don't see any place to upload the Requirement files  for BRD like upload requirements in web app.

5. - done - All the Artifacts in the Materdb, should have projectId ref, so that we know all project relavent artifacts and activities.

6.  - done - make sure All the Agnets needs to work in the Realm of Project Project ID.

7. - done - Re Branding this solution, project, Namespace, folder and DB name to the following
    GenesisNexus — full formal name
    GNex — short alias for CLI and internal references
    Genesis Nexus AI — if you want the AI suffix for marketing
    GN Studio — for the UI/dashboard

8. - done - hope you have found the fix for the deiplicated BRD and fixed the root cause. Use LLM in the BRD Generation with the template provided in the DB.




9. - done - All the agent action and Agent communications to be logged. this is logging needs to be controlled by flag in config file. this logs can be useed by the Agents to improve the working of agents. this can be very useful to look at the run time activities against the design time planning and communication.
    - Added AgentCommunicationEntry model and AgentCommType enum
    - Added EnableAgentCommunicationLogging flag to PipelineConfig (default: true)
    - Added CommunicationLog collection to AgentContext
    - WriteFeedback, ReadFeedback, DispatchFindingsAsFeedback now log when enabled
    - AgentResults storage in orchestrator also logged
    - AgentCommunicationLog DB entity with persistence after pipeline completion
    - GET /api/pipeline/communication-log endpoint for querying logs
    - UI checkbox "Agent Comm Log" added to pipeline config form

10. - done - In DatabaseAgent.cs we have " Do NOT use markdown code fences. Output raw C# only." this is hardcoded this should be coming from project teckstack config. SQL Scripts getting generated should be on the type of Database we have selected for example, Postgresql, MS SQL SERVER or MYSQL or roacle etc. GenerateDockerCompose is hardcoded, we should either to store these templates to be present in DB or files not in code. identofy similar pattern issues and fix them.
    - Added TechStackExtensions: DatabaseEngine(), DatabaseDockerImageByEngine(), DatabaseDefaultPort(), OutputFormatInstruction(), EfCoreProviderPackage(), EfCoreUseMethod(), DatabaseConnectionEnvVar()
    - GenerateDockerCompose now uses TechStackExtensions for DB image, messaging image, ports — supports PostgreSQL/MySQL/SQL Server
    - GenerateDefaultMigrationSql now generates DB-type-specific DDL (PostgreSQL/MySQL/SQL Server/Oracle)
    - GenerateRlsMigration now generates DB-type-specific RLS/VPD syntax
    - LLM prompt output format instructions now use context.OutputFormatInstruction() instead of hardcoded strings
    - LLM migration prompt standard columns description is now engine-agnostic
    - RequirementsExpanderAgent hardcoded "Use PostgreSQL-friendly types" fixed to use context.DatabaseEngine() 



11   -todo - i want to have a comprehensive integration unit test cases. to identigy gaps across the modules fix them. the unit test cases data should be more real world test data. can yon come up with your imagination. utlimate aim is to identify the gaps and fix them.


  the user story is junk and there are so many of them there is not context, component service etc,
following is one of the USE CASE.
 Execute Key Requirements from Source Files Workflow
Completed P2 Medium UseCase BRD
ID: UC-BRD-001-01
End-to-end workflow for Key Requirements from Source Files covering primary and alternative flows

Assigned: ServiceLayer

Iteration: 0   Created: 4/10/2026, 4:37:45 PM

Started: 4/10/2026, 4:38:46 PM

Completed: 4/10/2026, 4:38:46 PM
