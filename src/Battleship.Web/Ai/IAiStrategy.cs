using Battleship.Web.Game;

namespace Battleship.Web.Ai;

public interface IAiStrategy
{
    Coordinate ChooseShot(AiView view);
}
