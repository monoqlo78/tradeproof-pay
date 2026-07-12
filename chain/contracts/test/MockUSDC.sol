// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;

import {ERC20} from "@openzeppelin/contracts/token/ERC20/ERC20.sol";
import {Ownable} from "@openzeppelin/contracts/access/Ownable.sol";

/// @title MockUSDC — test-only settlement token (Testnet / local only)
/// @notice A 6-decimal ERC-20 used for the hackathon demo. It has NO monetary
///         value and must never be used on Mainnet. Only the owner can mint.
contract MockUSDC is ERC20, Ownable {
    constructor(address initialOwner) ERC20("Mock USD Coin", "MockUSDC") Ownable(initialOwner) {}

    function decimals() public pure override returns (uint8) {
        return 6;
    }

    function mint(address to, uint256 amount) external onlyOwner {
        _mint(to, amount);
    }
}
