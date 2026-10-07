"""Shared, sourced character metadata; independent of mesh/bone detection."""
import json
import re
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / 'assets'
CATALOGS = {
    'first': json.loads((ASSETS / 'character-ages.json').read_text(encoding='utf-8')),
    'second': json.loads((ASSETS / 'character-ages-2nd.json').read_text(encoding='utf-8')),
}

# Keep in sync with CharacterAgeCatalog.DefaultAdultBasis/DefaultAdultSource in
# CharacterAgeCatalog.cs; the .NET unit of the same change reads these strings.
DEFAULT_ADULT_BASIS = '年龄目录未登记该模型；目录未将其标为未成年，故按默认成年处理'
DEFAULT_ADULT_SOURCE = '游戏原始 asset_common_model.pac'

def chest_editing_allowed(status):
    """Policy: the catalog is a deny-list of minors. Only a catalogued minor is refused."""
    return str(status).lower() != 'minor'

def age_info(model_id, is_base_game_character=False, edition='first', definition_label=None):
    catalog = CATALOGS[edition]
    if model_id in catalog:
        return catalog[model_id]
    # A costume inherits a person's age only when the game's *definition* names
    # that person. A matching numeric prefix or scene alias alone is not
    # identity evidence, especially for resources assembled from reused parts.
    if is_base_game_character and re.fullmatch(r'chr\d{4}_c\d{2}', model_id, re.I):
        person = catalog.get(model_id[:7])
        if person and person.get('name') and definition_label and any(
                definition_label.startswith(person['name'] + separator) for separator in ('：', ':')):
            return {**person, 'basis': f'游戏名称表将该服装定义为「{definition_label}」；{person["basis"]}'}
    # Unlisted: defaulted adult, same rule for both editions.
    return {'status': 'defaulted-adult', 'age': None,
        'basis': DEFAULT_ADULT_BASIS, 'source': DEFAULT_ADULT_SOURCE}

def adult_eligible(model_id, is_base_game_character=False, edition='first', definition_label=None):
    info = age_info(model_id, is_base_game_character, edition, definition_label)
    return chest_editing_allowed(info['status']) and (info.get('age') is None or info['age'] >= 18)
