#pragma warning disable CS0436
namespace BazaarPlusPlus.Game.MonsterPreview;

internal sealed class PreviewBoardRequest
{
    public IPreviewDataSource DataSource { get; set; }

    public IBoardAnchorStrategy AnchorStrategy { get; set; }

    public PreviewBoardModel InitialModel { get; set; } = new PreviewBoardModel();

    public PreviewBoardPresentation Presentation { get; set; } = new PreviewBoardPresentation();

    public PreviewBoardDebugOptions Debug { get; set; } = new PreviewBoardDebugOptions();

    public BoardPose Pose { get; set; } = new BoardPose();
}
