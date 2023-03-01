using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using CTFServer.Models.Data;
using CTFServer.Models.Request.Edit;
using CTFServer.Utils;

namespace CTFServer.Models;

public class Challenge
{
    [Key]
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Challenge title
    /// <para>
    /// 题目名称
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "Title cannot be empty")]
    [MinLength(1, ErrorMessage = "Title too short")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Challenge content
    /// <para>
    /// 题目内容
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "Challenge content cannot be empty")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Whether the challenge is enabled
    /// <para>
    /// 是否启用题目
    /// </para>
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Challenge tag
    /// <para>
    /// 题目标签
    /// </para>
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChallengeTag Tag { get; set; } = ChallengeTag.Misc;

    /// <summary>
    /// Challenge type, cannot be changed after creation
    /// <para>
    /// 题目类型，创建后不可更改
    /// </para>
    /// </summary>
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    /// <summary>
    /// Challenge hints
    /// <para>
    /// 题目提示
    /// </para>
    /// </summary>
    public List<string>? Hints { get; set; }

    /// <summary>
    /// Flag template
    /// <para>
    /// Flag 模版，用于根据 Token 和题目、比赛信息生成 Flag
    /// </para>
    /// </summary>
    public string? FlagTemplate { get; set; }

    /// <summary>
    /// Container image
    /// <para>
    /// 镜像名称与标签
    /// </para>
    /// </summary>
    public string? ContainerImage { get; set; } = string.Empty;

    /// <summary>
    /// Memory limit (MB)
    /// <para>
    /// 运行内存限制 (MB)
    /// </para>
    /// </summary>
    public int? MemoryLimit { get; set; } = 64;

    /// <summary>
    /// Storage limit (MB)
    /// <para>
    /// 存储限制 (MB)
    /// </para>
    /// </summary>
    public int? StorageLimit { get; set; } = 256;

    /// <summary>
    /// CPU count limit
    /// <para>
    /// CPU 数量限制
    /// </para>
    /// </summary>
    public int? CPUCount { get; set; } = 1;

    /// <summary>
    /// Exposed container port
    /// <para>
    /// 镜像暴露端口
    /// </para>
    /// </summary>
    public int? ContainerExposePort { get; set; } = 80;

    /// <summary>
    /// Whether the container is privileged
    /// <para>
    /// 是否为特权容器
    /// </para>
    /// </summary>
    public bool? PrivilegedContainer { get; set; } = false;

    /// <summary>
    /// Number of times the challenge has been solved
    /// <para>
    /// 解决题目人数
    /// </para>
    /// </summary>
    [Required]
    public int AcceptedCount { get; set; } = 0;

    /// <summary>
    /// Number of answers submitted
    /// <para>
    /// 提交答案的数量
    /// </para>
    /// </summary>
    [Required]
    [JsonIgnore]
    public int SubmissionCount { get; set; } = 0;

    /// <summary>
    /// Original score
    /// <para>
    /// 初始分数
    /// </para>
    /// </summary>
    [Required]
    public int OriginalScore { get; set; } = 500;

    /// <summary>
    /// Minimum score ratio
    /// <para>
    /// 最低分数比例
    /// </para>
    /// </summary>
    [Required]
    [Range(0, 1)]
    public double MinScoreRate { get; set; } = 0.25;

    /// <summary>
    /// Difficulty factor
    /// <para>
    /// 难度系数
    /// </para>
    /// </summary>
    [Required]
    public double Difficulty { get; set; } = 5;

    /// <summary>
    /// Current score
    /// <para>
    /// 当前题目分值
    /// </para>
    /// </summary>
    [NotMapped]
    public int CurrentScore =>
        AcceptedCount <= 1 ? OriginalScore : (int)Math.Floor(
        OriginalScore * (MinScoreRate +
            (1.0 - MinScoreRate) * Math.Exp((1 - AcceptedCount) / Difficulty)
        ));

    /// <summary>
    /// Unified file name (only for dynamic attachment)
    /// <para>
    /// 下载文件名称，仅用于动态附件统一文件名
    /// </para>
    /// </summary>
    public string? FileName { get; set; } = "attachment";

    #region Db Relationship

    public int? AttachmentId { get; set; }

    /// <summary>
    /// Challenge attachment (dynamic attachment is stored in FlagInfoModel)
    /// <para>
    /// 题目附件（动态附件存放于 FlagContext）
    /// </para>
    /// </summary>
    public Attachment? Attachment { get; set; }

    public string? TestContainerId { get; set; }

    /// <summary>
    /// Test container
    /// <para>
    /// 测试容器
    /// </para>
    /// </summary>
    public Container? TestContainer { get; set; }

    /// <summary>
    /// Flags
    /// <para>
    /// 题目对应的 Flag 列表
    /// </para>
    /// </summary>
    public List<FlagContext> Flags { get; set; } = new();

    /// <summary>
    /// Submissions
    /// <para>
    /// 提交
    /// </para>
    /// </summary>
    public List<Submission> Submissions { get; set; } = new();

    /// <summary>
    /// Challenge instances
    /// <para>
    /// 赛题实例
    /// </para>
    /// </summary>
    public List<Instance> Instances { get; set; } = new();

    /// <summary>
    /// Teams that participated in this challenge
    /// <para>
    /// 激活赛题的队伍
    /// </para>
    /// </summary>
    public HashSet<Participation> Teams { get; set; } = new();

    public int GameId { get; set; }

    /// <summary>
    /// Game this challenge belongs to
    /// <para>
    /// 比赛对象
    /// </para>
    /// </summary>
    public Game Game { get; set; } = default!;

    #endregion Db Relationship

    internal string GenerateFlag(Participation part)
    {
        if (string.IsNullOrEmpty(FlagTemplate))
            return $"flag{Guid.NewGuid():B}";

        if (FlagTemplate.Contains("[TEAM_HASH]"))
        {
            var flag = FlagTemplate;
            if (FlagTemplate.StartsWith("[LEET]"))
                flag = Codec.Leet.LeetFlag(FlagTemplate[6..]);

            var hash = Codec.StrSHA256($"{part.Token}::{part.Game.PrivateKey}::{Id}");
            return flag.Replace("[TEAM_HASH]", hash[12..24]);
        }

        return Codec.Leet.LeetFlag(FlagTemplate);
    }

    internal string GenerateTestFlag()
    {
        if (string.IsNullOrEmpty(FlagTemplate))
            return "flag{GZCTF_dynamic_flag_test}";

        if (FlagTemplate.StartsWith("[LEET]"))
            return Codec.Leet.LeetFlag(FlagTemplate[6..]);

        return Codec.Leet.LeetFlag(FlagTemplate);
    }

    internal Challenge Update(ChallengeUpdateModel model)
    {
        Title = model.Title ?? Title;
        Content = model.Content ?? Content;
        Tag = model.Tag ?? Tag;
        Hints = model.Hints ?? Hints;
        IsEnabled = model.IsEnabled ?? IsEnabled;
        // only set FlagTemplate to null when it pass an empty string (but not null)
        FlagTemplate = model.FlagTemplate is null ? FlagTemplate :
            string.IsNullOrWhiteSpace(model.FlagTemplate) ? null : model.FlagTemplate;
        CPUCount = model.CPUCount ?? CPUCount;
        MemoryLimit = model.MemoryLimit ?? MemoryLimit;
        StorageLimit = model.StorageLimit ?? StorageLimit;
        ContainerImage = model.ContainerImage ?? ContainerImage;
        PrivilegedContainer = model.PrivilegedContainer ?? PrivilegedContainer;
        ContainerExposePort = model.ContainerExposePort ?? ContainerExposePort;
        OriginalScore = model.OriginalScore ?? OriginalScore;
        MinScoreRate = model.MinScoreRate ?? MinScoreRate;
        Difficulty = model.Difficulty ?? Difficulty;
        FileName = model.FileName ?? FileName;

        return this;
    }
}
