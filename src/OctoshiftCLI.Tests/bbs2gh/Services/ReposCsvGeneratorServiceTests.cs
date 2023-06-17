using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Octoshift.Models;
using OctoshiftCLI.BbsToGithub;
using OctoshiftCLI.BbsToGithub.Factories;
using OctoshiftCLI.Services;
using Xunit;

namespace OctoshiftCLI.Tests.BbsToGithub.Commands
{
    public class ReposCsvGeneratorServiceTests
    {
        private const string FULL_CSV_HEADER = "project,repo,url,last-commit-date,repo-size-in-bytes,attachments-size-in-bytes,is-archived,pr-count";
        private const string MINIMAL_CSV_HEADER = "project,repo,url,last-commit-date,repo-size-in-bytes,attachments-size-in-bytes";

        private readonly Mock<BbsApi> _mockBbsApi = TestHelpers.CreateMock<BbsApi>();
        private readonly Mock<BbsApiFactory> _mockBbsApiFactory = TestHelpers.CreateMock<BbsApiFactory>();
        private readonly Mock<BbsInspectorService> _mockBbsInspectorService = TestHelpers.CreateMock<BbsInspectorService>();
        private readonly Mock<BbsInspectorServiceFactory> _mockBbsInspectorServiceFactory = TestHelpers.CreateMock<BbsInspectorServiceFactory>();

        private const string BBS_SERVER_URL = "http://bbs-server-url";
        private const string BBS_PROJECT = "foo-project";
        private const string BBS_USERNAME = "bbs-username";
        private const string BBS_PASSWORD = "bbs-password";
        private const bool NO_SSL_VERIFY = true;
        private readonly IEnumerable<string> _bbsProjects = new List<string>() { BBS_PROJECT };
        private const string BBS_REPO = "foo-repo";
        private readonly IEnumerable<BbsRepository> _bbsRepos = new List<BbsRepository> { new() { Name = BBS_REPO } };

        private readonly ReposCsvGeneratorService _service;

        public ReposCsvGeneratorServiceTests()
        {
            _mockBbsInspectorServiceFactory.Setup(m => m.Create(_mockBbsApi.Object)).Returns(_mockBbsInspectorService.Object);
            _service = new ReposCsvGeneratorService(_mockBbsInspectorServiceFactory.Object, _mockBbsApiFactory.Object);
        }

        [Fact]
        public async Task Generate_Should_Return_Correct_Csv_For_One_Repo()
        {
            // Arrange
            var prCount = 822;
            var lastCommitDate = DateTime.Now;
            var archived = false;
            var repoSize = 10000UL;
            var attachmentsSize = 10000UL;

            _mockBbsApiFactory.Setup(m => m.Create(BBS_SERVER_URL, BBS_USERNAME, BBS_PASSWORD, NO_SSL_VERIFY)).Returns(_mockBbsApi.Object);

            _mockBbsApi.Setup(m => m.GetServerVersion()).ReturnsAsync("6.0.0");
            _mockBbsInspectorService.Setup(m => m.GetProjects()).ReturnsAsync(_bbsProjects);
            _mockBbsInspectorService.Setup(m => m.GetRepos(BBS_PROJECT)).ReturnsAsync(_bbsRepos);
            _mockBbsInspectorService.Setup(m => m.GetRepositoryPullRequestCount(BBS_PROJECT, BBS_REPO)).ReturnsAsync(prCount);
            _mockBbsInspectorService.Setup(m => m.GetLastCommitDate(BBS_PROJECT, BBS_REPO)).ReturnsAsync(lastCommitDate);
            _mockBbsApi.Setup(m => m.GetIsRepositoryArchived(BBS_PROJECT, BBS_REPO)).ReturnsAsync(archived);
            _mockBbsInspectorService.Setup(m => m.GetRepositoryAndAttachmentsSize(BBS_PROJECT, BBS_REPO, BBS_USERNAME, BBS_PASSWORD)).ReturnsAsync((repoSize, attachmentsSize));

            // Act
            var result = await _service.Generate(BBS_SERVER_URL, BBS_PROJECT, BBS_USERNAME, BBS_PASSWORD, NO_SSL_VERIFY);

            // Assert
            var expected = $"{FULL_CSV_HEADER}{Environment.NewLine}";
            expected += $"\"{BBS_PROJECT}\",\"{BBS_REPO}\",\"{BBS_SERVER_URL.TrimEnd('/')}/projects/{BBS_PROJECT}/repos/{BBS_REPO}\",\"{lastCommitDate:dd-MMM-yyyy hh:mm tt}\",\"{repoSize:N0}\",\"{attachmentsSize:N0}\",\"False\",{prCount}{Environment.NewLine}";

            result.Should().Be(expected);
        }

        [Fact]
        public async Task Generate_Should_Return_Correct_Csv_For_One_Repo_Without_Archived_Field_For_Outdated_BBS_Version()
        {
            // Arrange
            var prCount = 822;
            var lastCommitDate = DateTime.Now;
            var repoSize = 10000UL;
            var attachmentsSize = 10000UL;

            _mockBbsApiFactory.Setup(m => m.Create(BBS_SERVER_URL, BBS_USERNAME, BBS_PASSWORD, NO_SSL_VERIFY)).Returns(_mockBbsApi.Object);

            _mockBbsApi.Setup(m => m.GetServerVersion()).ReturnsAsync("5.0.0");
            _mockBbsInspectorService.Setup(m => m.GetProjects()).ReturnsAsync(_bbsProjects);
            _mockBbsInspectorService.Setup(m => m.GetRepos(BBS_PROJECT)).ReturnsAsync(_bbsRepos);
            _mockBbsInspectorService.Setup(m => m.GetRepositoryPullRequestCount(BBS_PROJECT, BBS_REPO)).ReturnsAsync(prCount);
            _mockBbsInspectorService.Setup(m => m.GetLastCommitDate(BBS_PROJECT, BBS_REPO)).ReturnsAsync(lastCommitDate);
            _mockBbsInspectorService.Setup(m => m.GetRepositoryAndAttachmentsSize(BBS_PROJECT, BBS_REPO, BBS_USERNAME, BBS_PASSWORD)).ReturnsAsync((repoSize, attachmentsSize));

            // Act
            var result = await _service.Generate(BBS_SERVER_URL, BBS_PROJECT, BBS_USERNAME, BBS_PASSWORD, NO_SSL_VERIFY);

            // Assert
            var expected = $"{FULL_CSV_HEADER.Replace(",is-archived", "")}{Environment.NewLine}";
            expected += $"\"{BBS_PROJECT}\",\"{BBS_REPO}\",\"{BBS_SERVER_URL.TrimEnd('/')}/projects/{BBS_PROJECT}/repos/{BBS_REPO}\",\"{lastCommitDate:dd-MMM-yyyy hh:mm tt}\",\"{repoSize:N0}\",\"{attachmentsSize:N0}\",{prCount}{Environment.NewLine}";

            result.Should().Be(expected);
            _mockBbsApi.Verify(m => m.GetIsRepositoryArchived(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Generate_Should_Return_Minimal_Csv_When_Minimal_Is_True()
        {
            // Arrange
            var lastCommitDate = DateTime.Now;
            var repoSize = 10000UL;
            var attachmentsSize = 10000UL;
            const bool minimal = true;

            _mockBbsApiFactory.Setup(m => m.Create(BBS_SERVER_URL, BBS_USERNAME, BBS_PASSWORD, NO_SSL_VERIFY)).Returns(_mockBbsApi.Object);

            _mockBbsApi.Setup(m => m.GetServerVersion()).ReturnsAsync("6.0.0");
            _mockBbsInspectorService.Setup(m => m.GetProjects()).ReturnsAsync(_bbsProjects);
            _mockBbsInspectorService.Setup(m => m.GetRepos(BBS_PROJECT)).ReturnsAsync(_bbsRepos);
            _mockBbsInspectorService.Setup(m => m.GetLastCommitDate(BBS_PROJECT, BBS_REPO)).ReturnsAsync(lastCommitDate);
            _mockBbsInspectorService.Setup(m => m.GetRepositoryAndAttachmentsSize(BBS_PROJECT, BBS_REPO, BBS_USERNAME, BBS_PASSWORD)).ReturnsAsync((repoSize, attachmentsSize));

            // Act
            var result = await _service.Generate(BBS_SERVER_URL, BBS_PROJECT, BBS_USERNAME, BBS_PASSWORD, NO_SSL_VERIFY, minimal);

            // Assert
            var expected = $"{MINIMAL_CSV_HEADER}{Environment.NewLine}";
            expected += $"\"{BBS_PROJECT}\",\"{BBS_REPO}\",\"{BBS_SERVER_URL.TrimEnd('/')}/projects/{BBS_PROJECT}/repos/{BBS_REPO}\",\"{lastCommitDate:dd-MMM-yyyy hh:mm tt}\",\"{repoSize:N0}\",\"{attachmentsSize:N0}\"{Environment.NewLine}";

            result.Should().Be(expected);
            _mockBbsInspectorService.Verify(m => m.GetPullRequestCount(It.IsAny<string>()), Times.Never);
        }
    }
}
