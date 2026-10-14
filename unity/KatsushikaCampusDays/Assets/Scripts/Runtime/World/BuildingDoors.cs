using System.Collections.Generic;

namespace KCD
{
    /// <summary>
    /// 建物の扉。ふつうは 1 棟に扉が 1 つで、扉の id は建物の id と同じ（玄関）。
    /// 2 つ目からの扉は自分の id を持ち、この表で建物に結ぶ（blender/kcd_lib/entrances.py の SECOND_DOORS と同じ表）。
    /// クエスト・入った記録・屋内の有無は建物の id で数え、扉の id は「どの扉から出入りしたか」にだけ使う。
    /// </summary>
    public static class BuildingDoors
    {
        /// <summary>2 つ目からの扉（扉の id, 建物の id）。講義棟の南東の玄関と、西の学生食堂側の扉 2 つ（南・北）。</summary>
        private static readonly (string Door, string Building)[] SecondDoors =
        {
            ("lecture_se", "lecture"),
            ("lecture_ws", "lecture"),
            ("lecture_wn", "lecture")
        };

        /// <summary>2 つ目からの扉の id の一覧。</summary>
        public static IEnumerable<string> SecondDoorIds
        {
            get
            {
                foreach ((string door, string _) in SecondDoors)
                {
                    yield return door;
                }
            }
        }

        /// <summary>扉の属する建物の id。表に無い扉は玄関なので、扉の id がそのまま建物の id。</summary>
        public static string BuildingOf(string doorId)
        {
            foreach ((string door, string building) in SecondDoors)
            {
                if (door == doorId)
                {
                    return building;
                }
            }

            return doorId;
        }

        /// <summary>玄関（扉の id が建物の id と同じ扉）か。</summary>
        public static bool IsMainDoor(string doorId)
        {
            return BuildingOf(doorId) == doorId;
        }

        /// <summary>建物の 2 つ目からの扉の id。無ければ空。</summary>
        public static List<string> SecondDoorsOf(string buildingId)
        {
            var doors = new List<string>();
            foreach ((string door, string building) in SecondDoors)
            {
                if (building == buildingId)
                {
                    doors.Add(door);
                }
            }

            return doors;
        }
    }
}
