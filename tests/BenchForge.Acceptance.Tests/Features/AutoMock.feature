@BD-AM-001 @BD-AM-002 @BD-AM-005
Feature: Experiment AutoMock behavior injection
  A test engineer can configure deterministic instrument faults without changing driver code.

  Scenario: Current changes only when a timed rule becomes active
    Given an AutoMock supply with a 24 ohm baseline and a fault at 5 seconds
    When the AutoMock supply is sampled at 4 seconds
    Then the AutoMock supply current is 0.5 amperes
    When the AutoMock supply is sampled at 5 seconds
    Then the AutoMock supply current is 2.5 amperes
    When the AutoMock supply output is disabled
    Then the AutoMock supply current is 0 amperes
