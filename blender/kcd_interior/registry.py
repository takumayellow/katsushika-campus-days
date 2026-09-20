"""建物 ID → プランモジュールの対応表。"""

from . import (plan_greenhouse, plan_gym, plan_kyoso, plan_lab, plan_lecture,
               plan_library, plan_research1, plan_research2)

PLANS = {
    "research1": plan_research1,
    "lecture": plan_lecture,
    "research2": plan_research2,
    "kyoso": plan_kyoso,
    "library": plan_library,
    "gym": plan_gym,
    "lab1": plan_lab,
    "lab2": plan_lab,
    "greenhouse": plan_greenhouse,
}

# 出力順（FBX とプレビューの並び）
ORDER = ["research1", "lecture", "research2", "kyoso", "library", "gym",
         "lab1", "lab2", "greenhouse"]

LABELS = {
    "research1": "第1研究棟",
    "lecture": "講義棟",
    "research2": "第2研究棟",
    "kyoso": "共創棟",
    "library": "図書館",
    "gym": "体育館",
    "lab1": "実験棟1",
    "lab2": "実験棟2",
    "greenhouse": "温室",
}


def get(bid):
    return PLANS.get(bid)
