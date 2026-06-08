using FaceSearchApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FaceSearchApp.Utils
{
    public static class AttributeParser
    {
        // ── 표시 그룹 정의 ───────────────────────────────────────────

        private static readonly List<(string GroupName, string[] Keys)> Groups = new()
        {
            ("기본 정보", ["gender_code","st_age","age_lower_limit","age_up_limit","st_pedestrian_angle","st_pose"]),
            ("상의",      ["coat_style","coat_color","coat_length","st_coat_pattern"]),
            ("하의",      ["trousers_len","trousers_color","st_trousers_pattern"]),
            ("신발/가방", ["shoes_style","shoes_color","bag_style"]),
            ("헤어/두상", ["hair_style","hair_color","cap_style","glass_style"]),
            ("행동/상태", ["st_smoking","st_phone_status","st_fishing","st_umbrella",
                           "st_glove_v2","st_hold_object_in_front_v2",
                           "st_reflective_clothes","st_oxygen_bottle","st_respirator_v2","st_uniform"]),
        };

        private static readonly HashSet<string> ColorKeys =
            ["coat_color", "trousers_color", "shoes_color", "hair_color"];

        // ── 속성명 한글 ──────────────────────────────────────────────

        private static readonly Dictionary<string, string> AttrNames = new()
        {
            ["gender_code"] = "성별",
            ["st_age"] = "연령대",
            ["age_lower_limit"] = "나이 하한",
            ["age_up_limit"] = "나이 상한",
            ["st_pedestrian_angle"] = "방향",
            ["st_pose"] = "자세",
            ["coat_style"] = "상의 스타일",
            ["coat_color"] = "상의 색상",
            ["coat_length"] = "소매 길이",
            ["st_coat_pattern"] = "상의 패턴",
            ["trousers_len"] = "하의 길이",
            ["trousers_color"] = "하의 색상",
            ["st_trousers_pattern"] = "하의 패턴",
            ["shoes_style"] = "신발 스타일",
            ["shoes_color"] = "신발 색상",
            ["bag_style"] = "가방",
            ["hair_style"] = "헤어",
            ["hair_color"] = "머리 색상",
            ["cap_style"] = "모자",
            ["glass_style"] = "안경",
            ["st_smoking"] = "흡연",
            ["st_phone_status"] = "휴대폰",
            ["st_fishing"] = "낚시",
            ["st_umbrella"] = "우산",
            ["st_glove_v2"] = "장갑",
            ["st_hold_object_in_front_v2"] = "물건 소지",
            ["st_reflective_clothes"] = "반사 의류",
            ["st_oxygen_bottle"] = "산소통",
            ["st_respirator_v2"] = "마스크",
            ["st_uniform"] = "유니폼",
        };

        // ── 값 한글 ──────────────────────────────────────────────────

        private static readonly Dictionary<string, string> ValueNames = new()
        {
            // 성별
            ["male"] = "남성",
            ["female"] = "여성",
            // 연령대
            ["st_adult"] = "성인",
            ["st_child"] = "아동",
            ["st_old"] = "노인",
            // 방향
            ["st_front"] = "정면",
            ["st_back"] = "후면",
            ["st_side"] = "측면",
            // 자세
            ["st_stand"] = "서있음",
            ["st_sit"] = "앉음",
            ["st_sit_on_the_seat"] = "의자에 앉음",
            ["st_sit_on_the_vehicle"] = "차량에 앉음",
            ["st_lie"] = "누워있음",
            ["st_sleep_on_the_table"] = "테이블에 누움",
            ["st_pose_agnostic"] = "불명확",
            // 상의 스타일
            ["t_shirt"] = "티셔츠",
            ["shirt"] = "셔츠",
            ["sweater"] = "스웨터",
            ["jacket"] = "재킷",
            ["coat_style_type_other"] = "기타",
            ["business_suit"] = "정장",
            ["dress"] = "원피스",
            ["long_coat"] = "롱코트",
            // 소매 길이
            ["short_sleeve"] = "반팔",
            ["long_sleeve"] = "긴팔",
            ["st_bareback"] = "민소매",
            ["coat_length_agnostic"] = "불명확",
            // 패턴
            ["st_pure"] = "단색",
            ["st_stripe"] = "스트라이프",
            ["st_lattic"] = "체크",
            ["st_design"] = "패턴",
            ["st_joint"] = "조인트",
            // 하의 길이
            ["trousers"] = "긴바지",
            ["shorts"] = "반바지",
            ["st_skirt"] = "치마",
            // 신발
            ["walking_shoes"] = "운동화",
            ["boots"] = "부츠",
            ["sandal"] = "샌들",
            ["leather_shoes"] = "구두",
            // 가방
            ["bag_style_type_without"] = "미소지",
            ["backpack"] = "백팩",
            ["shoulder_bag"] = "숄더백",
            ["hand_bag"] = "핸드백",
            ["waist_pack"] = "허리쌕",
            ["trolley"] = "캐리어",
            ["bag_style_agnostic"] = "불명확",
            // 헤어
            ["st_short"] = "단발",
            ["long"] = "장발",
            ["bald"] = "대머리",
            // 모자
            ["hat_style_type_without"] = "미착용",
            ["st_hard_hat"] = "안전모",
            ["cap"] = "캡모자",
            ["st_crash_helmet"] = "헬멧",
            ["bonnet"] = "보닛",
            ["bucket_hat"] = "버킷햇",
            ["st_hood"] = "후드",
            ["cap_style_agnostic"] = "불명확",
            // 안경
            ["glasses_style_type_without"] = "미착용",
            ["st_ordinary_glasses"] = "일반 안경",
            ["sunglasses"] = "선글라스",
            // 흡연
            ["st_smoking_without"] = "미흡연",
            ["st_smoking"] = "흡연",
            ["st_smoking_agnostic"] = "불명확",
            // 휴대폰
            ["st_normal"] = "정상",
            ["st_call"] = "통화중",
            ["st_play_phone"] = "폰 사용",
            ["st_take_photos"] = "촬영중",
            ["st_phone_status_agnostic"] = "불명확",
            // 장갑
            ["st_glove_without"] = "미착용",
            ["st_glove"] = "착용",
            ["st_glove_agnostic"] = "불명확",
            // 우산
            ["st_umbrella_without"] = "미소지",
            ["st_umbrella"] = "소지",
            // 낚시
            ["st_fishing_without"] = "해당없음",
            ["st_fishing"] = "낚시중",
            ["st_fishing_agnostic"] = "불명확",
            // 물건 소지
            ["st_hold_object_in_front_without"] = "미소지",
            ["st_hold_object_in_front_v2"] = "소지",
            ["st_hold_object_in_front_agnostic"] = "불명확",
            // 반사 의류
            ["st_reflective_clothes_without"] = "미착용",
            ["st_reflective_clothes"] = "착용",
            ["st_reflective_clothes_agnostic"] = "불명확",
            // 산소통
            ["st_oxygen_bottle_without"] = "미소지",
            ["st_oxygen_bottle"] = "소지",
            ["st_oxygen_bottle_agnostic"] = "불명확",
            // 마스크
            ["st_respirator_without"] = "미착용",
            ["st_respirator_full"] = "착용",
            ["st_respirator_agnostic"] = "불명확",
            // 유니폼
            ["st_common_clothing"] = "일반 복장",
            ["st_police_uniform"] = "경찰 제복",
            ["st_firefighter_uniform"] = "소방 제복",
            ["st_medical_uniform"] = "의료 복장",
            ["st_worker_uniform"] = "작업복",
            ["st_chef_uniform"] = "요리사 복장",
            ["st_office_uniform"] = "사무복",
            ["st_meituan_uniform"] = "기업 제복",
            ["st_eleme_uniform"] = "배달 제복",
            ["st_uniform_agnostic"] = "불명확",
        };

        // ── 색상 팔레트 ──────────────────────────────────────────────

        private static readonly Dictionary<string, (string Korean, string Hex)> ColorMap = new()
        {
            ["red"] = ("빨강", "#E74C3C"),
            ["gray"] = ("회색", "#95A5A6"),
            ["green"] = ("초록", "#27AE60"),
            ["white"] = ("흰색", "#ECEEEF"),
            ["blue"] = ("파랑", "#3498DB"),
            ["black"] = ("검정", "#2C3E50"),
            ["yellow"] = ("노랑", "#F1C40F"),
            ["purple"] = ("보라", "#9B59B6"),
        };

        // ── 공개 API ─────────────────────────────────────────────────

        /// <summary>API 응답을 표시용 AttributeResultItem 목록으로 변환 (인원 당 1개)</summary>
        public static List<AttributeResultItem> ParseAll(
            AttributeApiResponse response,
            BitmapImage? fullImage,
            string imageInfo)
        {
            var result = new List<AttributeResultItem>();
            var persons = response.Data?.Pedestrian;
            if (persons is null || persons.Count == 0) return result;

            int total = persons.Count;

            for (int i = 0; i < total; i++)
            {
                var person = persons[i];
                var croppedImage = CropPerson(fullImage, person.Detect);

                var item = new AttributeResultItem
                {
                    Image = croppedImage ?? fullImage,
                    FullImage = fullImage,
                    ImageInfo = imageInfo,
                    PersonLabel = total > 1 ? $"인원 {i + 1} / {total}" : string.Empty,
                    IsAnalyzing = false,
                };

                if (person.Attribute is not null)
                    item.AttributeGroups = ParseGroups(person.Attribute);

                result.Add(item);
            }

            return result;
        }

        // ── 내부: 그룹 파싱 ─────────────────────────────────────────

        private static List<AttributeDisplayGroup> ParseGroups(
            Dictionary<string, Dictionary<string, double>> attribute)
        {
            var result = new List<AttributeDisplayGroup>();

            foreach (var (groupName, keys) in Groups)
            {
                var items = new List<AttributeDisplayItem>();
                foreach (var key in keys)
                {
                    if (!attribute.TryGetValue(key, out var vals) || vals.Count == 0) continue;
                    var item = CreateItem(key, vals);
                    if (item is not null) items.Add(item);
                }
                if (items.Count > 0)
                    result.Add(new AttributeDisplayGroup { GroupName = groupName, Items = items });
            }

            return result;
        }

        private static AttributeDisplayItem? CreateItem(string key, Dictionary<string, double> values)
        {
            string name = AttrNames.TryGetValue(key, out var n) ? n : key;

            // 나이 하한/상한 특수 처리
            if (key == "age_lower_limit" && values.TryGetValue("age_lower_limit", out var lo))
                return new AttributeDisplayItem { Name = name, Value = $"{(int)lo}세 이상", Confidence = 1.0 };

            if (key == "age_up_limit" && values.TryGetValue("age_up_limit", out var hi))
                return new AttributeDisplayItem { Name = name, Value = $"{(int)hi}세 이하", Confidence = 1.0 };

            // 색상 속성
            if (ColorKeys.Contains(key))
            {
                var topColors = values
                    .Where(kv => ColorMap.ContainsKey(kv.Key))
                    .OrderByDescending(kv => kv.Value)
                    .Take(4)
                    .Select(kv => new ColorChip
                    {
                        KoreanName = ColorMap[kv.Key].Korean,
                        Hex = ColorMap[kv.Key].Hex,
                        Value = kv.Value
                    })
                    .ToList();

                var dominant = topColors.FirstOrDefault();
                return new AttributeDisplayItem
                {
                    Name = name,
                    Value = dominant?.KoreanName ?? "불명확",
                    Confidence = dominant?.Value ?? 0,
                    IsColor = true,
                    TopColors = topColors,
                };
            }

            // 일반 속성: 최댓값
            var best = values.MaxBy(kv => kv.Value);
            return new AttributeDisplayItem
            {
                Name = name,
                Value = ValueNames.TryGetValue(best.Key, out var vn) ? vn : best.Key,
                Confidence = best.Value,
            };
        }

        // ── 내부: 인물 크롭 ─────────────────────────────────────────

        private static BitmapImage? CropPerson(BitmapImage? source, List<List<int>>? detect)
        {
            if (source is null || detect is null || detect.Count < 2) return null;

            try
            {
                int x1 = Math.Max(0, detect[0][0]);
                int y1 = Math.Max(0, detect[0][1]);
                int x2 = Math.Min(source.PixelWidth, detect[1][0]);
                int y2 = Math.Min(source.PixelHeight, detect[1][1]);

                int w = x2 - x1, h = y2 - y1;
                if (w <= 0 || h <= 0) return null;

                var cropped = new CroppedBitmap(source, new Int32Rect(x1, y1, w, h));
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(cropped));

                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
