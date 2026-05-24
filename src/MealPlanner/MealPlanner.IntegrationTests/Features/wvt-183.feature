Feature: WVT-183 Ingredient recognition and matching

  As a user
  I want ingredients to be recognized and matched correctly regardless of how they are entered
  So that my meal plans and shopping lists are accurate and free of duplicate or mismatched items

  Background:
    Given there is a user named 'Alice'
    And 'Alice' is logged into Onebite

  Scenario: Ingredient names with different capitalizations are merged on the shopping list
    Given 'Alice' has a meal with ingredient 'Chicken Breast' in 'Cup' with amount '2' for today
    And 'Alice' has a meal with ingredient 'chicken breast' in 'Cup' with amount '3' for today
    When 'Alice' syncs her shopping list for today
    Then the shopping list shows 'chicken breast' once with amount '5'

  Scenario: Unit full name is accepted when adding an item to the shopping list
    Given 'Alice' navigates to the shopping list
    When 'Alice' adds a shopping list item with amount '2' unit 'Teaspoon' and name 'wvt183salt'
    Then 'wvt183salt' appears on the shopping list

  Scenario: Submitting the shopping list add form without a unit shows a validation error
    Given 'Alice' navigates to the shopping list
    When 'Alice' submits the shopping list add form with no unit selected for name 'wvt183flour'
    Then a shopping list error message is shown

  Scenario: Ingredients with convertible units are merged on the shopping list
    Given 'Alice' has a meal with ingredient 'wvt183sugar' in 'Teaspoon' with amount '3' for today
    And 'Alice' has a meal with ingredient 'wvt183sugar' in 'Tablespoon' with amount '1' for today
    When 'Alice' syncs her shopping list for today
    Then the shopping list shows 'wvt183sugar' once with amount '6' in 'tsp'

  Scenario: Adding the same ingredient with the same unit merges the quantities
    Given 'Alice' has 'wvt183butter' with amount '1' and measurement 'Cup' on the shopping list
    When 'Alice' navigates to the shopping list
    And 'Alice' adds a shopping list item with amount '2' unit 'Cup' and name 'wvt183butter'
    Then 'wvt183butter' appears on the shopping list with amount '3'

  Scenario: Adding an ingredient in an incompatible unit shows a conflict suggestion
    Given 'Alice' has 'wvt183garlic' with amount '12' and measurement 'Count' on the shopping list
    And 'Alice' navigates to the shopping list
    When 'Alice' adds a shopping list item with amount '1' unit 'Tablespoon' and name 'wvt183garlic'
    Then a conflict suggestion is shown for 'wvt183garlic'

  Scenario: Clicking Allow all on the auto-conflict popup keeps both incompatible items on the list
    Given 'Alice' has 'wvt183onion' with amount '3' and measurement 'Count' on the shopping list
    And 'Alice' has a meal with ingredient 'wvt183onion' in 'Tablespoon' with amount '2' for today
    When 'Alice' syncs her shopping list for today
    Then the auto-conflict popup is shown
    When 'Alice' clicks 'Allow all' on the auto-conflict popup
    Then 'wvt183onion' appears 2 times on the shopping list

  Scenario: Clicking Decline all on the auto-conflict popup removes the incoming item and leaves the existing one unchanged
    Given 'Alice' has 'wvt183basil' with amount '5' and measurement 'Count' on the shopping list
    And 'Alice' has a meal with ingredient 'wvt183basil' in 'Cup' with amount '1' for today
    When 'Alice' syncs her shopping list for today
    Then the auto-conflict popup is shown
    When 'Alice' clicks 'Decline all' on the auto-conflict popup
    Then 'wvt183basil' appears 1 times on the shopping list
    And 'wvt183basil' appears on the shopping list with amount '5'

  Scenario: The auto-conflict popup reappears when navigating back to the shopping list while a conflict exists
    Given 'Alice' has 'wvt183thyme' with amount '2' and measurement 'Count' on the shopping list
    And 'Alice' has a meal with ingredient 'wvt183thyme' in 'Tablespoon' with amount '1' for today
    When 'Alice' navigates to the shopping list
    Then the auto-conflict popup is shown
    When 'Alice' navigates away from the shopping list
    And 'Alice' navigates to the shopping list
    Then the auto-conflict popup is shown
