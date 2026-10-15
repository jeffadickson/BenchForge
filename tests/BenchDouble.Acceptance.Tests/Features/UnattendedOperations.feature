@BD-UOM-001 @BD-TEST-001
Feature: Unattended operations overcurrent alarm
  An operator monitoring a simulated power supply must receive one
  attributable overcurrent alarm when current crosses its configured limit.

  Scenario: Timed mock fault triggers one operator notification
    Given a simulated power supply monitored with a 2 ampere limit
    And the mock current is 0.5 amperes
    When the monitor observes the current
    Then no overcurrent alarm is raised
    When simulation time advances by 5 seconds
    And the mock current is 2.5 amperes
    And the monitor observes the current
    Then exactly one overcurrent alarm is raised at 5 simulated seconds
    When the monitor observes the current
    Then exactly one overcurrent alarm is raised at 5 simulated seconds

  Scenario: Normal operation does not notify the operator
    Given a simulated power supply monitored with a 2 ampere limit
    And the mock current is 0.5 amperes
    When the monitor observes the current
    Then no overcurrent alarm is raised
